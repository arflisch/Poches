using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Import;
using Poches.Core.Investments;
using Poches.Core.Models;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>
/// Imports the purchases, sales and dividends of a bank statement (Keytrade and most Belgian banks export CSV):
/// which column holds what (remembered per file format), a preview, then everything at once.
/// </summary>
public sealed partial class CsvImportViewModel : ObservableObject, ISheetViewModel
{
    /// <summary>Bank statements are small; anything bigger is not one.</summary>
    private const long MaxFileSize = 5 * 1024 * 1024;

    private const int PreviewRows = 40;

    private readonly BudgetStore _store;
    private readonly InvestmentService _investments;
    private readonly AppSettings _settings;
    private readonly BackupFilePicker _picker;
    private readonly MoneyFormatter _formatter;
    private List<List<string>> _rows = [];
    private string _headerKey = string.Empty;
    private Dictionary<string, IReadOnlyList<DailyClose>?> _rates = [];
    private CsvImportPlan? _plan;
    private TaxEnvelope _envelope = TaxEnvelope.Standard;
    private bool _building;
    private bool _rebuildRequested;

    public CsvImportViewModel(BudgetStore store, InvestmentService investments, AppSettings settings, BackupFilePicker picker)
    {
        _store = store;
        _investments = investments;
        _settings = settings;
        _picker = picker;
        _formatter = new MoneyFormatter(settings.Currency, culture: Localizer.Instance.Culture);
        IsBelgian = settings.BelgianTax;
        HasEnvelopes = settings.TaxCountry != TaxCountry.None;
        Envelopes = HoldingTypes.Envelopes(settings.TaxCountry)
            .Select((e, i) => new SelectableOption(e.ToString(), i, 2, o => SelectEnvelope(Enum.Parse<TaxEnvelope>(o.Value))) { Label = HoldingTypes.EnvelopeName(e, settings.TaxCountry), IsSelected = i == 0 })
            .ToList();
    }

    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    public bool IsBelgian { get; }

    public bool HasEnvelopes { get; }

    public IReadOnlyList<SelectableOption> Envelopes { get; }

    [ObservableProperty]
    private bool _hasFile;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    private string _fileText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<CsvFieldMapping> _mappings = [];

    [ObservableProperty]
    private bool _amountIsNet;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _foreignNote = string.Empty;

    [ObservableProperty]
    private bool _hasForeign;

    [ObservableProperty]
    private IReadOnlyList<CsvPreviewRow> _preview = [];

    [ObservableProperty]
    private string _moreText = string.Empty;

    [ObservableProperty]
    private bool _hasMore;

    [ObservableProperty]
    private string _importText = Loc.Get("Csv_Import");

    [ObservableProperty]
    private bool _canImport;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    partial void OnAmountIsNetChanged(bool value) => _ = RebuildAsync();

    private void SelectEnvelope(TaxEnvelope envelope)
    {
        _envelope = envelope;
        foreach (var option in Envelopes)
            option.IsSelected = option.Value == envelope.ToString();
    }

    [RelayCommand]
    private async Task PickAsync()
    {
        HasError = false;
        string text;
        try
        {
            await using var stream = await _picker.OpenCsvAsync();
            if (stream is null)
                return;
            text = await ReadTextAsync(stream);
        }
        catch (InvalidDataException)
        {
            ShowError(Loc.Get("Csv_TooBig"));
            return;
        }
        catch (IOException)
        {
            ShowError(Loc.Get("Csv_Unreadable"));
            return;
        }

        var (_, rows) = BankCsv.Parse(text);
        if (rows.Count < 2)
        {
            ShowError(Loc.Get("Csv_NoRows"));
            return;
        }
        _rows = rows;
        _rates = [];
        var headers = rows[0];
        _headerKey = string.Join('|', headers);
        var map = BankCsv.GuessMapping(headers);
        if (_settings.CsvMapping(_headerKey) is { } saved)
        {
            AmountIsNet = saved.GetValueOrDefault("amountIsNet") == 1;
            foreach (var (field, column) in saved.Where(kv => kv.Key != "amountIsNet"))
                map[field] = column;
        }
        var choices = new List<string> { "—" };
        choices.AddRange(headers.Select((h, i) => h.Length > 0 ? h : Loc.Format("Csv_Column", i + 1)));
        Mappings = BankCsv.Fields.Select(f => new CsvFieldMapping(f.Field, FieldName(f.Field), choices,
            map.TryGetValue(f.Field, out var column) ? column + 1 : 0, () => _ = RebuildAsync())).ToList();
        FileText = Loc.Format("Csv_RowsRead", rows.Count - 1);
        HasFile = true;
        await RebuildAsync();
    }

    /// <summary>UTF-8, or Windows-1252 for the banks that still export in it.</summary>
    private static async Task<string> ReadTextAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxFileSize)
                throw new InvalidDataException("The file is too big.");
        }
        var bytes = buffer.ToArray();
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private Dictionary<string, int> CurrentMap() =>
        Mappings.Where(m => m.SelectedIndex > 0).ToDictionary(m => m.Field, m => m.SelectedIndex - 1);

    private async Task RebuildAsync()
    {
        if (!HasFile)
            return;
        if (_building)
        {
            _rebuildRequested = true;
            return;
        }
        _building = true;
        IsWorking = true;
        try
        {
            do
            {
                _rebuildRequested = false;
                await BuildAsync();
            }
            while (_rebuildRequested);
        }
        finally
        {
            _building = false;
            IsWorking = false;
        }
    }

    private async Task BuildAsync()
    {
        var ops = BankCsv.ToOperations(_rows.Skip(1).Cast<IReadOnlyList<string>>().ToList(), CurrentMap(), AmountIsNet);
        if (IsBelgian)
            ops = BankCsv.WithDefaultWithholding(ops, InvestmentService.BelgianWithholding, AmountIsNet);
        // Market rates for the foreign currencies of the file, fetched once: they fill in a missing rate and
        // tell which way round the file's rates are quoted.
        var wanted = ops.Where(o => o.Error is null && o.Currency.Length == 3 && o.Currency != _settings.CurrencyCode && !_rates.ContainsKey(o.Currency))
            .Select(o => (o.Currency, o.Date!.Value)).ToList();
        if (wanted.Count > 0)
        {
            foreach (var (currency, rates) in await _investments.MarketRatesAsync(wanted, CancellationToken.None))
                _rates[currency] = rates;
        }
        var plan = CsvImportPlanner.Build(ops, await _store.GetHoldingsAsync(), await _store.GetPurchasesAsync(), await _store.GetSalesAsync(),
            await _store.GetDividendsAsync(), _settings.CurrencyCode, _rates, DateTime.Today);
        Show(plan);
    }

    private void Show(CsvImportPlan plan)
    {
        _plan = plan;
        var parts = new List<string>
        {
            Loc.Format("Csv_CountPurchases", plan.Purchases, Loc.Noun(plan.Purchases, "Purchase")),
            Loc.Format("Csv_CountSales", plan.Sales, Loc.Noun(plan.Sales, "Sale")),
            Loc.Format("Csv_CountDividends", plan.Dividends, Loc.Noun(plan.Dividends, "Dividend")),
        };
        if (plan.NewHoldings.Count > 0)
            parts.Add(Loc.Format("Csv_CountNewLines", plan.NewHoldings.Count, Loc.Noun(plan.NewHoldings.Count, "NewLine")));
        if (plan.Duplicates > 0)
            parts.Add(Loc.Format("Csv_CountDuplicates", plan.Duplicates));
        if (plan.Errors > 0)
            parts.Add(Loc.Format("Csv_CountErrors", plan.Errors));
        Summary = string.Join(" · ", parts);
        HasForeign = plan.Foreign > 0;
        ForeignNote = HasForeign ? Loc.Format("Csv_ForeignNote", plan.Foreign) : string.Empty;

        var newKeys = plan.NewHoldings.Select(h => h.Key).ToHashSet();
        Preview = plan.Rows.Take(PreviewRows).Select(r => new CsvPreviewRow(
            r.Operation.Date is { } date ? Loc.Date(date, "d MMM yy") : "?",
            KindName(r.Operation.Kind),
            r.LineName,
            r.NewHoldingKey is { } key && newKeys.Contains(key),
            r.Status switch
            {
                CsvRowStatus.Error => ErrorText(r.Error),
                CsvRowStatus.Duplicate => Loc.Get("Csv_Duplicate"),
                _ => Detail(r),
            },
            r.Status == CsvRowStatus.Error,
            r.Status == CsvRowStatus.Duplicate)).ToList();
        HasMore = plan.Rows.Count > PreviewRows;
        MoreText = HasMore ? Loc.Format("Csv_More", plan.Rows.Count - PreviewRows) : string.Empty;

        var count = plan.Purchases + plan.Sales + plan.Dividends;
        CanImport = count > 0;
        ImportText = count > 0 ? Loc.Format("Csv_ImportCount", count, Loc.Noun(count, "Operation")) : Loc.Get("Csv_Import");
    }

    private string Detail(CsvPlannedRow row)
    {
        var op = row.Operation;
        string Amount(decimal value) => row.Currency is { } currency ? $"{InvestmentText.Editable(value, 2)} {currency}" : _formatter.Format(value);
        return op.Kind == "dividend"
            ? Amount(op.Gross)
            : $"{InvestmentText.Quantity(op.Qty)} × {(row.Currency is { } c ? $"{InvestmentText.Editable(op.Price, 4)} {c}" : InvestmentText.Price(op.Price, _formatter))}";
    }

    private static string KindName(string? kind) => kind switch
    {
        "buy" => Loc.Get("Csv_KindBuy"),
        "sell" => Loc.Get("Csv_KindSell"),
        "dividend" => Loc.Get("Csv_KindDividend"),
        _ => "?",
    };

    private static string ErrorText(string? code) => code switch
    {
        "date" => Loc.Get("Csv_ErrorDate"),
        "kind" => Loc.Get("Csv_ErrorKind"),
        "name" => Loc.Get("Csv_ErrorName"),
        "gross" => Loc.Get("Csv_ErrorGross"),
        "qty" => Loc.Get("Csv_ErrorQuantity"),
        "price" => Loc.Get("Csv_ErrorPrice"),
        "currency" => Loc.Get("Csv_ErrorCurrency"),
        "rate" => Loc.Get("Csv_ErrorRate"),
        "future" => Loc.Get("Csv_ErrorFuture"),
        "unknown_line" => Loc.Get("Csv_ErrorUnknownLine"),
        _ => Loc.Get("Csv_ErrorOther"),
    };

    private static string FieldName(string field) => field switch
    {
        "date" => Loc.Get("Csv_FieldDate"),
        "kind" => Loc.Get("Csv_FieldKind"),
        "name" => Loc.Get("Csv_FieldName"),
        "isin" => Loc.Get("Csv_FieldIsin"),
        "symbol" => Loc.Get("Csv_FieldSymbol"),
        "fx" => Loc.Get("Csv_FieldRate"),
        "qty" => Loc.Get("Csv_FieldQuantity"),
        "price" => Loc.Get("Csv_FieldPrice"),
        "fees" => Loc.Get("Csv_FieldFees"),
        "tob" => Loc.Get("Csv_FieldTob"),
        "amount" => Loc.Get("Csv_FieldAmount"),
        "withholding" => Loc.Get("Csv_FieldWithholding"),
        "currency" => Loc.Get("Csv_FieldCurrency"),
        _ => field,
    };

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_plan is not { } plan || !CanImport)
            return;
        HasError = false;
        int count;
        try
        {
            count = await _store.ImportAsync(plan, HasEnvelopes ? _envelope : TaxEnvelope.Standard,
                i => Palette.Colors[i % Palette.Colors.Count], _settings.BelgianTax, DateTime.Today);
        }
        catch (BudgetException ex)
        {
            ShowError(ex.Error == BudgetError.SaleExceedsHoldings ? Loc.Get("Csv_Oversold") : Loc.Error(ex));
            return;
        }
        var map = CurrentMap();
        map["amountIsNet"] = AmountIsNet ? 1 : 0;
        _settings.SaveCsvMapping(_headerKey, map);
        SuccessToast.Show(Loc.Format("Csv_Imported", count, Loc.Noun(count, "Operation")));
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}

/// <summary>Which column of the file holds one field; index 0 is "none".</summary>
public sealed partial class CsvFieldMapping(string field, string label, IReadOnlyList<string> columns, int selectedIndex, Action changed) : ObservableObject
{
    public string Field { get; } = field;

    public string Label { get; } = label;

    public IReadOnlyList<string> Columns { get; } = columns;

    [ObservableProperty]
    private int _selectedIndex = selectedIndex;

    partial void OnSelectedIndexChanged(int value) => changed();
}

public sealed record CsvPreviewRow(string DateText, string KindText, string LineText, bool IsNewLine, string DetailText, bool IsError, bool IsDuplicate);
