using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

public sealed partial class EditPocketViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    private readonly BudgetStore _store;
    private readonly IDialogService _dialogs;
    private Pocket _pocket = new();

    public EditPocketViewModel(BudgetStore store, AppSettings settings, IDialogService dialogs)
    {
        _store = store;
        _dialogs = dialogs;
        Currency = settings.Currency;
    }

    public AppearanceSelection Appearance { get; } = new(Palette.Emojis);

    public string Currency { get; }

    [ObservableProperty]
    private string _title = Loc.Get("Edit_NewTitle");

    [ObservableProperty]
    private bool _isNew = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _hasGoal;

    [ObservableProperty]
    private string _goalText = string.Empty;

    [ObservableProperty]
    private string _initialAmountText = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && int.TryParse(id?.ToString(), out var pocketId))
            _ = LoadAsync(pocketId);
    }

    private async Task LoadAsync(int pocketId)
    {
        var summary = await _store.GetPocketSummaryAsync(pocketId);
        if (summary is null)
            return;

        _pocket = summary.Pocket;
        IsNew = false;
        Title = Loc.Get("Edit_EditTitle");
        Name = _pocket.Name;
        Appearance.Show(_pocket.Icon, _pocket.ColorHex);
        HasGoal = _pocket.Goal is not null;
        GoalText = _pocket.Goal is { } goal ? goal.ToString("0.##", Localizer.Instance.Culture) : string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        decimal? goal = null;
        if (HasGoal)
        {
            if (!AmountParser.TryParse(GoalText, out var parsedGoal) || parsedGoal <= 0)
            {
                ShowError(Loc.Get("Edit_InvalidGoal"));
                return;
            }
            goal = parsedGoal;
        }

        var initialAmount = 0m;
        if (IsNew && !string.IsNullOrWhiteSpace(InitialAmountText) && !AmountParser.TryParse(InitialAmountText, out initialAmount))
        {
            ShowError(Loc.Get("Error_InvalidInitialAmount"));
            return;
        }

        _pocket.Name = Name;
        _pocket.Icon = Appearance.Icon;
        _pocket.ColorHex = Appearance.ColorHex;
        _pocket.Goal = goal;

        try
        {
            await _store.SavePocketAsync(_pocket, initialAmount, Loc.Get("Edit_InitialNote"));
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
            Loc.Format("Edit_DeleteTitle", _pocket.Name),
            Loc.Get("Edit_DeleteText"),
            Loc.Get("Edit_DeleteConfirm"));
        if (!confirmed)
            return;

        await _store.DeletePocketAsync(_pocket.Id);
        Palette.Haptic();
        // Close this sheet and the detail page of the deleted pocket.
        await Shell.Current.GoToAsync("../..");
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    partial void OnNameChanged(string value) => HasError = false;

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}
