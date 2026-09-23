using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Services;

namespace Poches.ViewModels;

public sealed partial class EditPocketViewModel : ObservableObject, IQueryAttributable
{
    private const int GridColumns = 6;

    private readonly BudgetStore _store;
    private readonly IDialogService _dialogs;
    private Pocket _pocket = new();
    private string _selectedColorHex = Palette.Colors[0];

    public EditPocketViewModel(BudgetStore store, AppSettings settings, IDialogService dialogs)
    {
        _store = store;
        _dialogs = dialogs;
        Currency = settings.Currency;
        IconOptions = SelectableOption.Grid(Palette.Emojis, GridColumns, SelectIcon);
        ColorOptions = SelectableOption.Grid(Palette.Colors, GridColumns, SelectColor);
        SelectIcon(IconOptions[0]);
        SelectColor(ColorOptions[0]);
    }

    public IReadOnlyList<SelectableOption> IconOptions { get; }

    public IReadOnlyList<SelectableOption> ColorOptions { get; }

    public string Currency { get; }

    [ObservableProperty]
    private string _title = "Nouvelle poche";

    [ObservableProperty]
    private bool _isNew = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _selectedIcon = Palette.Emojis[0];

    [ObservableProperty]
    private Color _selectedColor = Color.FromArgb(Palette.Colors[0]);

    [ObservableProperty]
    private Color _selectedSoftColor = Palette.Soft(Color.FromArgb(Palette.Colors[0]));

    [ObservableProperty]
    private Brush _selectedBrush = Palette.HeroBrush(Color.FromArgb(Palette.Colors[0]));

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
        Title = "Modifier la poche";
        Name = _pocket.Name;
        SelectIcon(IconOptions.FirstOrDefault(o => o.Value == _pocket.Icon) ?? new SelectableOption(_pocket.Icon, 0, 1, _ => { }));
        SelectColor(ColorOptions.FirstOrDefault(o => o.Value.Equals(_pocket.ColorHex, StringComparison.OrdinalIgnoreCase))
            ?? new SelectableOption(_pocket.ColorHex, 0, 1, _ => { }));
        HasGoal = _pocket.Goal is not null;
        GoalText = _pocket.Goal is { } goal ? goal.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) : string.Empty;
    }

    private void SelectIcon(SelectableOption option)
    {
        SelectedIcon = option.Value;
        Select(IconOptions, option.Value);
    }

    private void SelectColor(SelectableOption option)
    {
        _selectedColorHex = option.Value;
        SelectedColor = option.Color;
        SelectedSoftColor = Palette.Soft(option.Color);
        SelectedBrush = Palette.HeroBrush(option.Color);
        Select(ColorOptions, option.Value);
        foreach (var icon in IconOptions)
        {
            icon.Accent = SelectedColor;
            icon.AccentSoft = SelectedSoftColor;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        decimal? goal = null;
        if (HasGoal)
        {
            if (!AmountParser.TryParse(GoalText, out var parsedGoal) || parsedGoal <= 0)
            {
                ShowError("Indique un objectif valide, par exemple 2 000.");
                return;
            }
            goal = parsedGoal;
        }

        var initialAmount = 0m;
        if (IsNew && !string.IsNullOrWhiteSpace(InitialAmountText) && !AmountParser.TryParse(InitialAmountText, out initialAmount))
        {
            ShowError("Le montant de départ n'est pas valide.");
            return;
        }

        _pocket.Name = Name;
        _pocket.Icon = SelectedIcon;
        _pocket.ColorHex = _selectedColorHex;
        _pocket.Goal = goal;

        try
        {
            await _store.SavePocketAsync(_pocket, initialAmount);
            Palette.Haptic();
            await Shell.Current.GoToAsync("..");
        }
        catch (BudgetException ex)
        {
            ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            $"Supprimer « {_pocket.Name} » ?",
            "La poche et tout son historique seront définitivement supprimés.",
            "Supprimer");
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

    private static void Select(IEnumerable<SelectableOption> options, string value)
    {
        foreach (var option in options)
            option.IsSelected = string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase);
    }
}
