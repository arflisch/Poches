using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Investments;
using Poches.Core.Models;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>
/// Adds or edits a purchase: the line (an existing one, or a new one found by name, ticker or ISIN), the date,
/// the unit price and the quantity or amount invested, plus fees.
/// </summary>
public sealed partial class PurchaseEditViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    private readonly BudgetStore _store;
    private readonly InvestmentService _investments;
    private readonly IDialogService _dialogs;
    private readonly MoneyFormatter _formatter;
    private Purchase _purchase = new() { Date = DateTime.Today };
    private Dictionary<int, string?> _symbols = [];
    private bool _loaded;

    public PurchaseEditViewModel(BudgetStore store, InvestmentService investments, AppSettings settings, IDialogService dialogs, SymbolSearchViewModel search)
    {
        _store = store;
        _investments = investments;
        _dialogs = dialogs;
        // Amounts being typed are always shown, even in privacy mode.
        _formatter = new MoneyFormatter(settings.Currency, culture: Localizer.Instance.Culture);
        Currency = settings.Currency;
        Search = search;
        Search.Picked = OnSymbolPicked;
        Search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SymbolSearchViewModel.CurrentPrice) && IsNewHolding)
                _ = ShowPriceHintAsync(0);
        };
        Types = HoldingTypes.All
            .Select((t, i) => new SelectableOption(t.ToString(), i, 4, o => SelectType(Enum.Parse<HoldingType>(o.Value)))
            {
                Label = $"{HoldingTypes.Icon(t)}\n{HoldingTypes.Name(t)}",
            })
            .ToList();
        Modes =
        [
            new SelectableOption("quantity", 0, 2, _ => ByAmount = false) { Label = Loc.Get("Inv_ModeQuantity") },
            new SelectableOption("amount", 1, 2, _ => ByAmount = true) { Label = Loc.Get("Inv_ModeAmount") },
        ];
        SelectType(HoldingType.Etf);
        OnByAmountChanged(false);
    }

    public SymbolSearchViewModel Search { get; }

    public IReadOnlyList<SelectableOption> Types { get; }

    public IReadOnlyList<SelectableOption> Modes { get; }

    public string Currency { get; }

    [ObservableProperty]
    private IReadOnlyList<HoldingChoice> _holdings = [];

    [ObservableProperty]
    private string _title = Loc.Get("Inv_NewPurchase");

    [ObservableProperty]
    private bool _isNew = true;

    [ObservableProperty]
    private bool _isNewHolding = true;

    [ObservableProperty]
    private string _holdingName = string.Empty;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _account = string.Empty;

    [ObservableProperty]
    private HoldingType _type = HoldingType.Etf;

    [ObservableProperty]
    private DateTime? _date = DateTime.Today;

    [ObservableProperty]
    private string _priceText = string.Empty;

    [ObservableProperty]
    private string _pricePlaceholder = ExamplePrice;

    private static string ExamplePrice => 52.40m.ToString("0.00", Localizer.Instance.Culture);

    [ObservableProperty]
    private bool _byAmount;

    [ObservableProperty]
    private string _sizeLabel = string.Empty;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _feesText = string.Empty;

    [ObservableProperty]
    private string _preview = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (_loaded)
            return;
        _loaded = true;
        int? purchaseId = query.TryGetValue("id", out var id) && int.TryParse(id?.ToString(), out var p) ? p : null;
        int? holdingId = query.TryGetValue("holdingId", out var h) && int.TryParse(h?.ToString(), out var hid) ? hid : null;
        _ = LoadAsync(purchaseId, holdingId);
    }

    private async Task LoadAsync(int? purchaseId, int? holdingId)
    {
        var holdings = await _store.GetHoldingsAsync();
        if (purchaseId is { } pid && await _store.GetPurchaseAsync(pid) is { } purchase)
        {
            _purchase = purchase;
            IsNew = false;
            Title = Loc.Get("Inv_EditPurchase");
            Date = purchase.Date;
            PriceText = InvestmentText.Editable(purchase.UnitPrice);
            SizeText = InvestmentText.Editable(purchase.Quantity);
            FeesText = purchase.FeesCents > 0 ? InvestmentText.Editable(purchase.Fees, 2) : string.Empty;
            holdingId = purchase.HoldingId;
        }

        var selected = holdingId ?? holdings.LastOrDefault()?.Id ?? 0;
        _symbols = holdings.ToDictionary(x => x.Id, x => x.Symbol);
        Holdings = holdings
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new HoldingChoice(x.Id, x.Name, x.Symbol ?? HoldingTypes.Name(x.Type), Color.FromArgb(x.ColorHex), SelectHolding))
            .Append(new HoldingChoice(0, Loc.Get("Inv_NewHolding"), Loc.Get("Inv_NewHoldingHint"), Colors.Transparent, SelectHolding))
            .ToList();
        SelectHolding(selected);
    }

    private void SelectHolding(int holdingId)
    {
        foreach (var choice in Holdings)
            choice.IsSelected = choice.Id == holdingId;
        IsNewHolding = holdingId == 0;
        _purchase.HoldingId = holdingId;
        _ = ShowPriceHintAsync(holdingId);
    }

    /// <summary>The current quote of the chosen line is a better hint than the example price.</summary>
    private async Task ShowPriceHintAsync(int holdingId)
    {
        var price = holdingId == 0 ? Search.CurrentPrice : null;
        if (holdingId != 0 && _symbols.GetValueOrDefault(holdingId) is { } symbol)
        {
            try
            {
                // Stays offline unless online quotes are on: the last stored quote is used then.
                var quote = await _investments.PreviewAsync(symbol, CancellationToken.None);
                price = quote is { Status: QuoteStatus.Ok } ? quote.Price : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Only a hint: keep the example price.
            }
            if (_purchase.HoldingId != holdingId)
                return;
        }
        PricePlaceholder = price is { } p ? InvestmentText.Editable(p, 4) : ExamplePrice;
    }

    private void SelectType(HoldingType type)
    {
        Type = type;
        foreach (var option in Types)
            option.IsSelected = option.Value == type.ToString();
    }

    private void OnSymbolPicked(SymbolMatch match)
    {
        if (string.IsNullOrWhiteSpace(HoldingName))
            HoldingName = HoldingTypes.ShortName(match.Name);
        SelectType(HoldingTypes.FromKind(match.Kind));
        if (string.IsNullOrWhiteSpace(Code) && LooksLikeIsin(Search.Query))
            Code = Search.Query.Trim().ToUpperInvariant();
    }

    partial void OnByAmountChanged(bool value)
    {
        foreach (var option in Modes)
            option.IsSelected = option.Value == (value ? "amount" : "quantity");
        SizeLabel = value ? Loc.Format("Inv_AmountInvested", Currency) : Loc.Get("Inv_Quantity");
        // Switching keeps what was typed meaningful: a quantity becomes the matching amount and back.
        if (AmountParser.TryParseDecimal(PriceText, 6, out var price) && price > 0
            && AmountParser.TryParseDecimal(SizeText, 6, out var size) && size > 0)
            SizeText = InvestmentText.Editable(value ? decimal.Round(size * price, 2) : decimal.Round(size / price, 6));
        UpdatePreview();
    }

    partial void OnPriceTextChanged(string value) => UpdatePreview();

    partial void OnSizeTextChanged(string value) => UpdatePreview();

    partial void OnFeesTextChanged(string value) => UpdatePreview();

    private bool TryRead(out decimal quantity, out decimal price, out decimal fees)
    {
        quantity = 0;
        fees = 0;
        if (!AmountParser.TryParseDecimal(PriceText, 6, out price) || price <= 0)
            return false;
        if (!AmountParser.TryParseDecimal(SizeText, 6, out var size) || size <= 0)
            return false;
        quantity = ByAmount ? decimal.Round(size / price, 6) : size;
        return string.IsNullOrWhiteSpace(FeesText) || (AmountParser.TryParse(FeesText, out fees) && fees >= 0);
    }

    private void UpdatePreview()
    {
        HasError = false;
        Preview = TryRead(out var quantity, out var price, out var fees) && quantity > 0
            ? Loc.Format("Inv_PurchasePreview", InvestmentText.Quantity(quantity), InvestmentText.Price(price, _formatter), _formatter.Format(quantity * price + fees))
            : string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!AmountParser.TryParseDecimal(PriceText, 6, out var price) || price <= 0)
        {
            ShowError(Loc.Get("Inv_InvalidPrice"));
            return;
        }
        if (!TryRead(out var quantity, out _, out var fees))
        {
            ShowError(ByAmount ? Loc.Get("Inv_InvalidAmount") : Loc.Get("Inv_InvalidQuantity"));
            return;
        }

        Holding? newHolding = null;
        if (IsNewHolding)
        {
            var existing = await _store.GetHoldingsAsync();
            newHolding = new Holding
            {
                Name = HoldingName,
                Code = Code,
                Account = Account,
                Type = Type,
                Symbol = Search.Symbol,
                // Each new line takes the next colour of the palette, so the chart stays readable.
                ColorHex = Palette.Colors[existing.Count % Palette.Colors.Count],
            };
        }

        _purchase.Date = (Date ?? DateTime.Today).Date;
        _purchase.Quantity = quantity;
        _purchase.UnitPrice = price;
        _purchase.Fees = fees;

        try
        {
            var saved = await _store.SavePurchaseAsync(_purchase, DateTime.Today, newHolding);
            SuccessToast.Show(Loc.Get("Toast_Saved"));
            await Shell.Current.GoToAsync("..");
            if (newHolding is not null)
                await Shell.Current.GoToAsync($"{Routes.Holding}?id={saved.HoldingId}");
        }
        catch (BudgetException ex)
        {
            ShowError(Loc.Error(ex));
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            Loc.Get("Inv_DeletePurchaseTitle"),
            Loc.Get("Inv_DeletePurchaseText"),
            Loc.Get("Edit_DeleteConfirm"));
        if (!confirmed)
            return;

        await _store.DeletePurchaseAsync(_purchase.Id);
        Palette.Haptic();
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    private static bool LooksLikeIsin(string text) =>
        text.Trim().Length == 12 && char.IsAsciiLetter(text.Trim()[0]) && char.IsAsciiLetter(text.Trim()[1]) && text.Trim().All(char.IsAsciiLetterOrDigit);
}

/// <summary>A line offered in the purchase sheet; <see cref="Id"/> 0 stands for "new line".</summary>
public sealed partial class HoldingChoice(int id, string name, string caption, Color color, Action<int> select) : ObservableObject
{
    public int Id { get; } = id;

    public string Name { get; } = name;

    public string Caption { get; } = caption;

    public Color Color { get; } = color;

    public bool IsNew => Id == 0;

    public bool IsExisting => Id != 0;

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => select(Id);
}
