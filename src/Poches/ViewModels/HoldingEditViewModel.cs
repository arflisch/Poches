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
/// Edits an investment line: name, type, account, colour, quote symbol and manual price, running costs and target
/// weight; under the Belgian option its tax envelope, stock-exchange tax rate, 31/12/2025 value and Reynders tax.
/// </summary>
public sealed partial class HoldingEditViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    private static readonly TaxEnvelope[] EnvelopeOrder =
        [TaxEnvelope.Standard, TaxEnvelope.PensionSavings, TaxEnvelope.LongTermSavings, TaxEnvelope.Branch21, TaxEnvelope.Branch23];

    private const string AutoTob = "auto";

    private readonly BudgetStore _store;
    private readonly InvestmentService _investments;
    private readonly IDialogService _dialogs;
    private Holding _holding = new();
    private string _tobChoice = AutoTob;
    private decimal? _suggestedTer;
    private bool _loaded;

    public HoldingEditViewModel(BudgetStore store, InvestmentService investments, AppSettings settings, IDialogService dialogs, SymbolSearchViewModel search)
    {
        _store = store;
        _investments = investments;
        _dialogs = dialogs;
        Currency = settings.Currency;
        IsBelgian = settings.BelgianTax;
        Search = search;
        Search.Picked = match =>
        {
            SelectType(HoldingTypes.FromKind(match.Kind));
            _ = SuggestTerAsync();
        };
        Envelopes = EnvelopeOrder
            .Select((e, i) => new SelectableOption(e.ToString(), i, 2, o => SelectEnvelope(Enum.Parse<TaxEnvelope>(o.Value))) { Label = HoldingTypes.EnvelopeName(e) })
            .ToList();
        Types = HoldingTypes.All
            .Select((t, i) => new SelectableOption(t.ToString(), i, 4, o => SelectType(Enum.Parse<HoldingType>(o.Value)))
            {
                Label = $"{HoldingTypes.Icon(t)}\n{HoldingTypes.Name(t)}",
            })
            .ToList();
        ColorOptions = SelectableOption.Grid(Palette.Colors, 6, o => SelectColor(o.Value));
    }

    public SymbolSearchViewModel Search { get; }

    public bool IsBelgian { get; }

    public IReadOnlyList<SelectableOption> Envelopes { get; }

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _tobRates = [];

    [ObservableProperty]
    private bool _isSheltered;

    [ObservableProperty]
    private string _referencePriceText = string.Empty;

    [ObservableProperty]
    private bool _reynders;

    [ObservableProperty]
    private string _terText = string.Empty;

    [ObservableProperty]
    private string _terSuggestion = string.Empty;

    [ObservableProperty]
    private bool _hasTerSuggestion;

    [ObservableProperty]
    private string _targetText = string.Empty;

    public IReadOnlyList<SelectableOption> Types { get; }

    public IReadOnlyList<SelectableOption> ColorOptions { get; }

    public string Currency { get; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _account = string.Empty;

    [ObservableProperty]
    private string _manualPriceText = string.Empty;

    [ObservableProperty]
    private DateTime? _manualPriceDate = DateTime.Today;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (_loaded || !query.TryGetValue("id", out var id) || !int.TryParse(id?.ToString(), out var holdingId))
            return;
        _loaded = true;
        _ = LoadAsync(holdingId);
    }

    private async Task LoadAsync(int holdingId)
    {
        if (await _store.GetHoldingAsync(holdingId) is not { } holding)
            return;
        _holding = holding;
        Name = holding.Name;
        Code = holding.Code ?? string.Empty;
        Account = holding.Account ?? string.Empty;
        ManualPriceText = holding.ManualPrice is { } price ? InvestmentText.Editable(price) : string.Empty;
        ManualPriceDate = holding.ManualPriceDate ?? DateTime.Today;
        ReferencePriceText = holding.ReferencePrice is { } reference ? InvestmentText.Editable(reference) : string.Empty;
        Reynders = holding.Reynders;
        TerText = holding.Ter is { } ter ? InvestmentText.Editable(ter * 100, 4) : string.Empty;
        TargetText = holding.Target is { } target ? InvestmentText.Editable(target * 100, 2) : string.Empty;
        _tobChoice = holding.TobRate is { } tob ? tob.ToString(System.Globalization.CultureInfo.InvariantCulture) : AutoTob;
        SelectEnvelope(holding.Envelope);
        SelectType(holding.Type);
        SelectColor(holding.ColorHex);
        Search.Show(holding.Symbol, holding.Code ?? holding.Name);
        await SuggestTerAsync();
    }

    /// <summary>A fund's running costs as Yahoo gives them, offered when none is entered.</summary>
    private async Task SuggestTerAsync()
    {
        try
        {
            _suggestedTer = await _investments.SuggestedTerAsync(Search.Symbol, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _suggestedTer = null;
        }
        HasTerSuggestion = _suggestedTer is not null;
        TerSuggestion = _suggestedTer is { } ter ? Loc.Format("Inv_TerSuggestion", InvestmentText.Percent((double)ter, 2)) : string.Empty;
    }

    private void SelectEnvelope(TaxEnvelope envelope)
    {
        _holding.Envelope = envelope;
        IsSheltered = BelgianTax.IsSheltered(envelope);
        foreach (var option in Envelopes)
            option.IsSelected = option.Value == envelope.ToString();
    }

    /// <summary>The stock-exchange tax rates; "automatic" follows the type of the line.</summary>
    private void BuildTobRates()
    {
        var automatic = BelgianTax.DefaultTobRate(_holding.Type);
        string[] values = [AutoTob, "0.0012", "0.0035", "0.0132", "0"];
        TobRates = values.Select((v, i) => new SelectableOption(v, i, 3, o => SelectTob(o.Value))
        {
            Label = v == AutoTob
                ? Loc.Format("Inv_TobAuto", InvestmentText.Percent((double)automatic, 2))
                : v == "0" ? Loc.Get("Inv_TobNoneShort") : InvestmentText.Percent((double)decimal.Parse(v, System.Globalization.CultureInfo.InvariantCulture), 2),
            IsSelected = v == _tobChoice,
        }).ToList();
    }

    private void SelectTob(string value)
    {
        _tobChoice = value;
        foreach (var option in TobRates)
            option.IsSelected = option.Value == value;
    }

    private void SelectType(HoldingType type)
    {
        _holding.Type = type;
        foreach (var option in Types)
            option.IsSelected = option.Value == type.ToString();
        BuildTobRates();
    }

    private void SelectColor(string hex)
    {
        _holding.ColorHex = hex;
        foreach (var option in ColorOptions)
            option.IsSelected = string.Equals(option.Value, hex, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnNameChanged(string value) => HasError = false;

    partial void OnManualPriceTextChanged(string value) => HasError = false;

    [RelayCommand]
    private void UseSuggestedTer()
    {
        if (_suggestedTer is { } ter)
            TerText = InvestmentText.Editable(ter * 100, 4);
    }

    private static bool TryOptionalPositive(string text, int decimals, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (!AmountParser.TryParseDecimal(text, decimals, out var parsed) || parsed < 0)
            return false;
        value = parsed;
        return true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        decimal? manual = null;
        if (!string.IsNullOrWhiteSpace(ManualPriceText))
        {
            if (!AmountParser.TryParseDecimal(ManualPriceText, 6, out var price) || price <= 0)
            {
                ShowError(Loc.Get("Inv_InvalidPrice"));
                return;
            }
            manual = price;
        }

        if (!TryOptionalPositive(TerText, 4, out var ter) || ter > 5)
        {
            ShowError(Loc.Get("Inv_InvalidTer"));
            return;
        }
        if (!TryOptionalPositive(TargetText, 2, out var target) || target > 100)
        {
            ShowError(Loc.Get("Inv_InvalidTarget"));
            return;
        }
        if (!TryOptionalPositive(ReferencePriceText, 6, out var reference) || reference == 0)
        {
            ShowError(Loc.Get("Inv_InvalidPrice"));
            return;
        }

        _holding.Ter = ter / 100;
        _holding.Target = target is > 0 ? target / 100 : null;
        if (IsBelgian)
        {
            _holding.ReferencePrice = reference;
            _holding.Reynders = Reynders;
            _holding.TobRate = _tobChoice == AutoTob ? null : decimal.Parse(_tobChoice, System.Globalization.CultureInfo.InvariantCulture);
        }
        _holding.Name = Name;
        _holding.Code = Code;
        _holding.Account = Account;
        _holding.Symbol = Search.Symbol;
        _holding.ManualPrice = manual;
        _holding.ManualPriceDate = manual is null ? null : (ManualPriceDate ?? DateTime.Today).Date;
        try
        {
            await _store.SaveHoldingAsync(_holding);
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
            Loc.Format("Edit_DeleteTitle", _holding.Name),
            Loc.Get("Inv_DeleteHoldingText"),
            Loc.Get("Edit_DeleteConfirm"));
        if (!confirmed)
            return;

        await _store.DeleteHoldingAsync(_holding.Id);
        Palette.Haptic();
        await Shell.Current.GoToAsync("../..");
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}
