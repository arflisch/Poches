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
/// the unit price and the quantity or amount invested, plus fees, the stock-exchange tax (computed under the Belgian
/// option), the currency of the statement and the part paid with dividends.
/// </summary>
public sealed partial class PurchaseEditViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    private readonly BudgetStore _store;
    private readonly InvestmentService _investments;
    private readonly IDialogService _dialogs;
    private readonly MoneyFormatter _formatter;
    private readonly bool _belgian;
    private Purchase _purchase = new() { Date = DateTime.Today };
    private Dictionary<int, Holding> _holdingsById = [];
    private IReadOnlyList<Dividend> _dividends = [];
    private IReadOnlyList<Purchase> _otherPurchases = [];
    private decimal _dividendRoom;
    private bool _loaded;
    private bool _settingTob;
    private bool _tobEdited;

    public PurchaseEditViewModel(BudgetStore store, InvestmentService investments, AppSettings settings, IDialogService dialogs, SymbolSearchViewModel search,
        CurrencyFieldViewModel currencyField)
    {
        _store = store;
        _investments = investments;
        _dialogs = dialogs;
        _belgian = settings.BelgianTax;
        ShowTob = _belgian;
        // Amounts being typed are always shown, even in privacy mode.
        _formatter = new MoneyFormatter(settings.Currency, culture: Localizer.Instance.Culture);
        CurrencyField = currencyField;
        CurrencyField.Changed += () =>
        {
            OnPropertyChanged(nameof(Currency));
            UpdatePreview();
        };
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

    public CurrencyFieldViewModel CurrencyField { get; }

    public IReadOnlyList<SelectableOption> Types { get; }

    public IReadOnlyList<SelectableOption> Modes { get; }

    /// <summary>What prices and fees are typed in: the operation's currency.</summary>
    public string Currency => CurrencyField.AmountUnit;

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
    private bool _showTob;

    [ObservableProperty]
    private string _tobText = string.Empty;

    [ObservableProperty]
    private string _tobHint = string.Empty;

    [ObservableProperty]
    private string _fromDividendsText = string.Empty;

    [ObservableProperty]
    private bool _showFromDividends;

    [ObservableProperty]
    private string _dividendRoomText = string.Empty;

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
        _dividends = await _store.GetDividendsAsync();
        if (purchaseId is { } pid && await _store.GetPurchaseAsync(pid) is { } purchase)
        {
            _purchase = purchase;
            IsNew = false;
            Title = Loc.Get("Inv_EditPurchase");
            CurrencyField.Load(purchase.Currency, Operations.IsForeign(purchase) ? purchase.Rate : null);
            Date = purchase.Date;
            PriceText = InvestmentText.Editable(purchase.UnitPrice);
            SizeText = InvestmentText.Editable(purchase.Quantity);
            FeesText = purchase.FeesCents > 0 ? InvestmentText.Editable(purchase.Fees, 2) : string.Empty;
            // A recorded tax stays as it was: it came from the statement.
            _tobEdited = true;
            SetTob(purchase.TobCents > 0 ? InvestmentText.Editable(purchase.Tob, 2) : string.Empty);
            ShowTob |= purchase.TobCents > 0;
            FromDividendsText = purchase.FromDividendsCents > 0 ? InvestmentText.Editable(purchase.FromDividends, 2) : string.Empty;
            holdingId = purchase.HoldingId;
        }
        _otherPurchases = (await _store.GetPurchasesAsync()).Where(p => p.Id != _purchase.Id).ToList();
        await CurrencyField.SetDateAsync(Date ?? DateTime.Today);
        UpdateDividendRoom();

        var selected = holdingId ?? holdings.LastOrDefault()?.Id ?? 0;
        _holdingsById = holdings.ToDictionary(x => x.Id);
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
        UpdateTob();
        _ = ShowPriceHintAsync(holdingId);
    }

    /// <summary>The current quote of the chosen line is a better hint than the example price.</summary>
    private async Task ShowPriceHintAsync(int holdingId)
    {
        var price = holdingId == 0 ? Search.CurrentPrice : null;
        // Quotes are in the portfolio currency: no hint for a price typed in another one.
        if (CurrencyField.IsForeign)
            price = null;
        else if (holdingId != 0 && _holdingsById.GetValueOrDefault(holdingId)?.Symbol is { } symbol)
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
        UpdateTob();
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

    partial void OnPriceTextChanged(string value)
    {
        UpdateTob();
        UpdatePreview();
    }

    partial void OnSizeTextChanged(string value)
    {
        UpdateTob();
        UpdatePreview();
    }

    partial void OnFeesTextChanged(string value) => UpdatePreview();

    partial void OnTobTextChanged(string value)
    {
        if (!_settingTob)
            _tobEdited = !string.IsNullOrWhiteSpace(value);
        UpdatePreview();
    }

    partial void OnFromDividendsTextChanged(string value) => HasError = false;

    partial void OnDateChanged(DateTime? value)
    {
        if (!_loaded)
            return;
        _ = CurrencyField.SetDateAsync(value ?? DateTime.Today);
        UpdateDividendRoom();
    }

    private void SetTob(string text)
    {
        _settingTob = true;
        TobText = text;
        _settingTob = false;
    }

    /// <summary>Under the Belgian option the tax follows the amount until the user types the one of the statement.</summary>
    private void UpdateTob()
    {
        if (!_belgian)
            return;
        var rate = IsNewHolding || !_holdingsById.TryGetValue(_purchase.HoldingId, out var holding)
            ? BelgianTax.DefaultTobRate(Type)
            : BelgianTax.TobRateFor(holding);
        TobHint = rate > 0 ? Loc.Format("Inv_TobHint", InvestmentText.Percent((double)rate, 2)) : Loc.Get("Inv_TobNone");
        if (_tobEdited)
            return;
        var amount = TryReadTrade(out var quantity, out var price) ? quantity * price : 0;
        var tob = BelgianTax.Tob(rate, amount);
        SetTob(tob > 0 ? InvestmentText.Editable(tob, 2) : string.Empty);
    }

    /// <summary>Dividends received and not reinvested yet can pay part of the purchase.</summary>
    private void UpdateDividendRoom()
    {
        _dividendRoom = DividendTools.Room(_dividends, _otherPurchases, (Date ?? DateTime.Today).Date);
        ShowFromDividends = _dividendRoom > 0 || !string.IsNullOrWhiteSpace(FromDividendsText);
        DividendRoomText = Loc.Format("Inv_DividendRoom", _formatter.Format(_dividendRoom));
    }

    private bool TryReadTrade(out decimal quantity, out decimal price)
    {
        quantity = 0;
        if (!AmountParser.TryParseDecimal(PriceText, 6, out price) || price <= 0)
            return false;
        if (!AmountParser.TryParseDecimal(SizeText, 6, out var size) || size <= 0)
            return false;
        quantity = ByAmount ? decimal.Round(size / price, 6) : size;
        return true;
    }

    private bool TryRead(out decimal quantity, out decimal price, out decimal fees, out decimal tob)
    {
        fees = 0;
        tob = 0;
        if (!TryReadTrade(out quantity, out price))
            return false;
        return (string.IsNullOrWhiteSpace(FeesText) || (AmountParser.TryParse(FeesText, out fees) && fees >= 0))
            && (string.IsNullOrWhiteSpace(TobText) || (AmountParser.TryParse(TobText, out tob) && tob >= 0));
    }

    private void UpdatePreview()
    {
        HasError = false;
        if (!TryRead(out var quantity, out var price, out var fees, out var tob) || quantity <= 0)
        {
            Preview = string.Empty;
            return;
        }
        var total = quantity * price + fees + tob;
        if (CurrencyField.IsForeign)
        {
            var unit = CurrencyField.Code;
            Preview = Loc.Format("Inv_PurchasePreview", InvestmentText.Quantity(quantity), $"{InvestmentText.Editable(price, 4)} {unit}", $"{InvestmentText.Editable(total, 2)} {unit}")
                + " · " + Loc.Format("Inv_AboutBase", _formatter.Format(total / CurrencyField.PreviewRate));
            return;
        }
        Preview = Loc.Format("Inv_PurchasePreview", InvestmentText.Quantity(quantity), InvestmentText.Price(price, _formatter), _formatter.Format(total));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!AmountParser.TryParseDecimal(PriceText, 6, out var price) || price <= 0)
        {
            ShowError(Loc.Get("Inv_InvalidPrice"));
            return;
        }
        if (!TryRead(out var quantity, out _, out var fees, out var tob))
        {
            ShowError(ByAmount ? Loc.Get("Inv_InvalidAmount") : Loc.Get("Inv_InvalidQuantity"));
            return;
        }
        if (!CurrencyField.TryRead(out var currency, out var rate))
        {
            ShowError(Loc.Get("Inv_InvalidRate"));
            return;
        }
        decimal fromDividends = 0;
        if (!string.IsNullOrWhiteSpace(FromDividendsText) && (!AmountParser.TryParse(FromDividendsText, out fromDividends) || fromDividends < 0))
        {
            ShowError(Loc.Get("Inv_InvalidFromDividends"));
            return;
        }
        if (fromDividends > _dividendRoom + 0.005m)
        {
            ShowError(Loc.Format("Inv_FromDividendsTooMuch", _formatter.Format(_dividendRoom)));
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
                Envelope = TaxEnvelope.Standard,
                // Each new line takes the next colour of the palette, so the chart stays readable.
                ColorHex = Palette.Colors[existing.Count % Palette.Colors.Count],
            };
        }

        _purchase.Date = (Date ?? DateTime.Today).Date;
        _purchase.Quantity = quantity;
        _purchase.UnitPrice = price;
        _purchase.Fees = fees;
        _purchase.Tob = tob;
        _purchase.Currency = currency;
        _purchase.FxRateMicros = rate is { } r ? Units.ToMicros(r) : null;
        _purchase.FromDividendsCents = fromDividends > 0 ? Money.ToCents(fromDividends) : null;

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

        try
        {
            await _store.DeletePurchaseAsync(_purchase.Id);
        }
        catch (BudgetException ex)
        {
            ShowError(Loc.Error(ex));
            return;
        }
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
