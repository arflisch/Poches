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
/// Adds or edits a sale (units, price, fees and stock-exchange tax; the oldest units go first) or a dividend (gross
/// amount and tax withheld), in the currency of the bank statement.
/// </summary>
public sealed partial class OperationEditViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    public const string SaleKind = "sell";
    public const string DividendKind = "dividend";

    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    private readonly BudgetStore _store;
    private readonly InvestmentService _investments;
    private readonly IDialogService _dialogs;
    private readonly MoneyFormatter _formatter;
    private readonly bool _belgian;
    private Dictionary<int, Holding> _holdingsById = [];
    private IReadOnlyList<Purchase> _purchases = [];
    private IReadOnlyList<Sale> _sales = [];
    private Sale _sale = new() { Date = DateTime.Today };
    private Dividend _dividend = new() { Date = DateTime.Today };
    private int _holdingId;
    private bool _loaded;
    private bool _settingTob;
    private bool _tobEdited;
    private bool _settingWithholding;
    private bool _withholdingEdited;

    public OperationEditViewModel(BudgetStore store, InvestmentService investments, AppSettings settings, IDialogService dialogs, CurrencyFieldViewModel currencyField)
    {
        _store = store;
        _investments = investments;
        _dialogs = dialogs;
        _belgian = settings.BelgianTax;
        _formatter = new MoneyFormatter(settings.Currency, culture: Localizer.Instance.Culture);
        CurrencyField = currencyField;
        CurrencyField.Changed += () =>
        {
            OnPropertyChanged(nameof(Currency));
            UpdatePreview();
        };
        ShowTob = _belgian;
        CanUseBelgianWithholding = _belgian;
    }

    public CurrencyFieldViewModel CurrencyField { get; }

    public string Currency => CurrencyField.AmountUnit;

    [ObservableProperty]
    private IReadOnlyList<HoldingChoice> _holdings = [];

    [ObservableProperty]
    private bool _isSale = true;

    [ObservableProperty]
    private bool _isDividend;

    [ObservableProperty]
    private bool _isNew = true;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private DateTime? _date = DateTime.Today;

    [ObservableProperty]
    private string _quantityText = string.Empty;

    [ObservableProperty]
    private string _heldText = string.Empty;

    [ObservableProperty]
    private bool _hasHeld;

    [ObservableProperty]
    private string _priceText = string.Empty;

    [ObservableProperty]
    private string _pricePlaceholder = "0";

    [ObservableProperty]
    private string _feesText = string.Empty;

    [ObservableProperty]
    private bool _showTob;

    [ObservableProperty]
    private string _tobText = string.Empty;

    [ObservableProperty]
    private string _tobHint = string.Empty;

    [ObservableProperty]
    private string _grossText = string.Empty;

    [ObservableProperty]
    private string _withholdingText = string.Empty;

    public bool CanUseBelgianWithholding { get; }

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
        var kind = query.TryGetValue("kind", out var k) ? k?.ToString() : SaleKind;
        int? id = query.TryGetValue("id", out var i) && int.TryParse(i?.ToString(), out var parsed) ? parsed : null;
        int? holdingId = query.TryGetValue("holdingId", out var h) && int.TryParse(h?.ToString(), out var hid) ? hid : null;
        var detected = query.TryGetValue("detected", out var d) ? d as DetectedDividend : null;
        _ = LoadAsync(kind == DividendKind, id, holdingId ?? detected?.HoldingId, detected);
    }

    private async Task LoadAsync(bool dividend, int? id, int? holdingId, DetectedDividend? detected)
    {
        IsDividend = dividend;
        IsSale = !dividend;
        var holdings = await _store.GetHoldingsAsync();
        _holdingsById = holdings.ToDictionary(x => x.Id);
        _purchases = await _store.GetPurchasesAsync();
        _sales = await _store.GetSalesAsync();
        Title = dividend ? Loc.Get("Inv_NewDividend") : Loc.Get("Inv_NewSale");

        if (!dividend && id is { } saleId && await _store.GetSaleAsync(saleId) is { } sale)
        {
            _sale = sale;
            IsNew = false;
            Title = Loc.Get("Inv_EditSale");
            CurrencyField.Load(sale.Currency, Operations.IsForeign(sale) ? sale.Rate : null);
            Date = sale.Date;
            QuantityText = InvestmentText.Editable(sale.Quantity);
            PriceText = InvestmentText.Editable(sale.UnitPrice);
            FeesText = sale.FeesCents > 0 ? InvestmentText.Editable(sale.Fees, 2) : string.Empty;
            _tobEdited = true;
            SetTob(sale.TobCents > 0 ? InvestmentText.Editable(sale.Tob, 2) : string.Empty);
            ShowTob |= sale.TobCents > 0;
            holdingId = sale.HoldingId;
        }
        else if (dividend && id is { } dividendId && await _store.GetDividendAsync(dividendId) is { } recorded)
        {
            _dividend = recorded;
            IsNew = false;
            Title = Loc.Get("Inv_EditDividend");
            CurrencyField.Load(recorded.Currency, Operations.IsForeign(recorded) ? recorded.Rate : null);
            Date = recorded.Date;
            GrossText = InvestmentText.Editable(recorded.Gross, 2);
            // A recorded withholding stays as it was: it came from the statement.
            _withholdingEdited = true;
            SetWithholding(recorded.WithholdingCents > 0 ? InvestmentText.Editable(recorded.Withholding, 2) : string.Empty);
            holdingId = recorded.HoldingId;
        }
        else if (detected is not null)
        {
            // From a detected distribution: the amounts of the statement replace the estimate.
            CurrencyField.Load(detected.FxRate is null ? null : detected.Currency, detected.FxRate);
            Date = detected.Date;
            GrossText = InvestmentText.Editable(detected.Gross, 2);
        }
        await CurrencyField.SetDateAsync(Date ?? DateTime.Today);

        // A sale needs units: lines already sold out are only offered when editing one of their sales.
        var offered = holdings.Where(x => dividend || x.Id == holdingId
            || InvestmentMath.HeldOn(_purchases, _sales, x.Id, DateTime.Today) > 0).ToList();
        Holdings = offered
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new HoldingChoice(x.Id, x.Name, x.Symbol ?? HoldingTypes.Name(x.Type), Color.FromArgb(x.ColorHex), SelectHolding))
            .ToList();
        SelectHolding(holdingId is { } chosen && offered.Any(x => x.Id == chosen) ? chosen : offered.FirstOrDefault()?.Id ?? 0);
    }

    private void SelectHolding(int holdingId)
    {
        _holdingId = holdingId;
        foreach (var choice in Holdings)
            choice.IsSelected = choice.Id == holdingId;
        UpdateHeld();
        UpdateTob();
        _ = ShowPriceHintAsync(holdingId);
    }

    private async Task ShowPriceHintAsync(int holdingId)
    {
        decimal? price = null;
        if (!IsDividend && !CurrencyField.IsForeign && _holdingsById.GetValueOrDefault(holdingId)?.Symbol is { } symbol)
        {
            try
            {
                var quote = await _investments.PreviewAsync(symbol, CancellationToken.None);
                price = quote is { Status: QuoteStatus.Ok } ? quote.Price : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Only a hint.
            }
            if (_holdingId != holdingId)
                return;
        }
        PricePlaceholder = price is { } p ? InvestmentText.Editable(p, 4) : "0";
    }

    /// <summary>Units held on the sale's date, this sale left out.</summary>
    private decimal Held() =>
        InvestmentMath.HeldOn(_purchases, _sales, _holdingId, (Date ?? DateTime.Today).Date, _sale.Id == 0 ? null : _sale.Id);

    private void UpdateHeld()
    {
        if (IsDividend)
            return;
        var held = Held();
        HasHeld = held > 0;
        HeldText = Loc.Format("Inv_SellAll", InvestmentText.Quantity(held));
    }

    partial void OnDateChanged(DateTime? value)
    {
        if (!_loaded)
            return;
        _ = CurrencyField.SetDateAsync(value ?? DateTime.Today);
        UpdateHeld();
    }

    partial void OnQuantityTextChanged(string value)
    {
        UpdateTob();
        UpdatePreview();
    }

    partial void OnPriceTextChanged(string value)
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

    partial void OnGrossTextChanged(string value)
    {
        UpdateWithholding();
        UpdatePreview();
    }

    partial void OnWithholdingTextChanged(string value)
    {
        if (!_settingWithholding)
            _withholdingEdited = !string.IsNullOrWhiteSpace(value);
        UpdatePreview();
    }

    private void SetWithholding(string text)
    {
        _settingWithholding = true;
        WithholdingText = text;
        _settingWithholding = false;
    }

    /// <summary>Under the Belgian option the précompte (30 %) follows the gross amount until the user types the one of the statement.</summary>
    private void UpdateWithholding()
    {
        if (!_belgian || _withholdingEdited)
            return;
        SetWithholding(AmountParser.TryParse(GrossText, out var gross) && gross > 0
            ? InvestmentText.Editable(Math.Round(gross * InvestmentService.BelgianWithholding, 2), 2)
            : string.Empty);
    }

    private void SetTob(string text)
    {
        _settingTob = true;
        TobText = text;
        _settingTob = false;
    }

    private void UpdateTob()
    {
        if (!_belgian || IsDividend || !_holdingsById.TryGetValue(_holdingId, out var holding))
            return;
        var rate = BelgianTax.TobRateFor(holding);
        TobHint = rate > 0 ? Loc.Format("Inv_TobHint", InvestmentText.Percent((double)rate, 2)) : Loc.Get("Inv_TobNone");
        if (_tobEdited)
            return;
        var tob = TryReadSale(out var quantity, out var price) ? BelgianTax.Tob(rate, quantity * price) : 0;
        SetTob(tob > 0 ? InvestmentText.Editable(tob, 2) : string.Empty);
    }

    private bool TryReadSale(out decimal quantity, out decimal price)
    {
        price = 0;
        return AmountParser.TryParseDecimal(QuantityText, 6, out quantity) && quantity > 0
            && AmountParser.TryParseDecimal(PriceText, 6, out price) && price > 0;
    }

    private static bool TryOptional(string text, out decimal value)
    {
        value = 0;
        return string.IsNullOrWhiteSpace(text) || (AmountParser.TryParse(text, out value) && value >= 0);
    }

    private string Amount(decimal value) =>
        CurrencyField.IsForeign ? $"{InvestmentText.Editable(value, 2)} {CurrencyField.Code}" : _formatter.Format(value);

    private string InBase(decimal value) =>
        CurrencyField.IsForeign ? " · " + Loc.Format("Inv_AboutBase", _formatter.Format(value / CurrencyField.PreviewRate)) : string.Empty;

    private void UpdatePreview()
    {
        HasError = false;
        if (IsDividend)
        {
            Preview = AmountParser.TryParse(GrossText, out var gross) && gross > 0 && TryOptional(WithholdingText, out var withholding) && withholding <= gross
                ? Loc.Format("Inv_DividendPreview", Amount(gross - withholding), Amount(gross), Amount(withholding)) + InBase(gross - withholding)
                : string.Empty;
            return;
        }
        Preview = TryReadSale(out var quantity, out var price) && TryOptional(FeesText, out var fees) && TryOptional(TobText, out var tob)
            ? Loc.Format("Inv_SalePreview", InvestmentText.Quantity(quantity), Amount(quantity * price - fees - tob)) + InBase(quantity * price - fees - tob)
            : string.Empty;
    }

    [RelayCommand]
    private void SellAll()
    {
        QuantityText = InvestmentText.Editable(Held());
    }

    /// <summary>The Belgian précompte mobilier: 30 % of the gross amount.</summary>
    [RelayCommand]
    private void UseBelgianWithholding()
    {
        _withholdingEdited = false;
        UpdateWithholding();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_holdingId == 0)
        {
            ShowError(Loc.Get("Inv_ChooseLine"));
            return;
        }
        if (!CurrencyField.TryRead(out var currency, out var rate))
        {
            ShowError(Loc.Get("Inv_InvalidRate"));
            return;
        }
        long? rateMicros = rate is { } r ? Units.ToMicros(r) : null;
        var date = (Date ?? DateTime.Today).Date;
        try
        {
            if (IsDividend)
            {
                if (!AmountParser.TryParse(GrossText, out var gross) || gross <= 0)
                {
                    ShowError(Loc.Get("Inv_InvalidGross"));
                    return;
                }
                if (!TryOptional(WithholdingText, out var withholding) || withholding > gross)
                {
                    ShowError(Loc.Get("Inv_InvalidWithholding"));
                    return;
                }
                _dividend.HoldingId = _holdingId;
                _dividend.Date = date;
                _dividend.Gross = gross;
                _dividend.Withholding = withholding;
                _dividend.Currency = currency;
                _dividend.FxRateMicros = rateMicros;
                await _store.SaveDividendAsync(_dividend, DateTime.Today);
            }
            else
            {
                if (!AmountParser.TryParseDecimal(QuantityText, 6, out var quantity) || quantity <= 0)
                {
                    ShowError(Loc.Get("Inv_InvalidQuantity"));
                    return;
                }
                if (!AmountParser.TryParseDecimal(PriceText, 6, out var price) || price <= 0)
                {
                    ShowError(Loc.Get("Inv_InvalidPrice"));
                    return;
                }
                if (!TryOptional(FeesText, out var fees) || !TryOptional(TobText, out var tob))
                {
                    ShowError(Loc.Get("Inv_InvalidFees"));
                    return;
                }
                _sale.HoldingId = _holdingId;
                _sale.Date = date;
                _sale.Quantity = quantity;
                _sale.UnitPrice = price;
                _sale.Fees = fees;
                _sale.Tob = tob;
                _sale.Currency = currency;
                _sale.FxRateMicros = rateMicros;
                await _store.SaveSaleAsync(_sale, DateTime.Today);
            }
            SuccessToast.Show(Loc.Get("Toast_Saved"));
            await Shell.Current.GoToAsync("..");
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
            IsDividend ? Loc.Get("Inv_DeleteDividendTitle") : Loc.Get("Inv_DeleteSaleTitle"),
            Loc.Get("Inv_DeleteOperationText"),
            Loc.Get("Edit_DeleteConfirm"));
        if (!confirmed)
            return;
        try
        {
            if (IsDividend)
                await _store.DeleteDividendAsync(_dividend.Id);
            else
                await _store.DeleteSaleAsync(_sale.Id);
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
}
