using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Investments;
using Poches.Core.Models;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>Edits an investment line: name, type, account, colour, quote symbol and manual price.</summary>
public sealed partial class HoldingEditViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    private readonly BudgetStore _store;
    private readonly IDialogService _dialogs;
    private Holding _holding = new();
    private bool _loaded;

    public HoldingEditViewModel(BudgetStore store, AppSettings settings, IDialogService dialogs, SymbolSearchViewModel search)
    {
        _store = store;
        _dialogs = dialogs;
        Currency = settings.Currency;
        Search = search;
        Search.Picked = match => SelectType(HoldingTypes.FromKind(match.Kind));
        Types = HoldingTypes.All
            .Select((t, i) => new SelectableOption(t.ToString(), i, 4, o => SelectType(Enum.Parse<HoldingType>(o.Value)))
            {
                Label = $"{HoldingTypes.Icon(t)}\n{HoldingTypes.Name(t)}",
            })
            .ToList();
        ColorOptions = SelectableOption.Grid(Palette.Colors, 6, o => SelectColor(o.Value));
    }

    public SymbolSearchViewModel Search { get; }

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
        SelectType(holding.Type);
        SelectColor(holding.ColorHex);
        Search.Show(holding.Symbol, holding.Code ?? holding.Name);
    }

    private void SelectType(HoldingType type)
    {
        _holding.Type = type;
        foreach (var option in Types)
            option.IsSelected = option.Value == type.ToString();
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
