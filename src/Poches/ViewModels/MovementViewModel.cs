using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

public enum MovementMode
{
    Deposit,
    Withdrawal,
    Transfer,
}

public sealed partial class MovementViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    private const int MaxIntegerDigits = 9;
    private static readonly Color DefaultAccent = Color.FromArgb("#4F46E5");

    private readonly BudgetStore _store;
    private readonly IDialogService _dialogs;
    private readonly MoneyFormatter _formatter;
    private readonly AppSettings _settings;
    private List<PocketChoice> _pockets = [];
    private int? _requestedPocketId;

    public MovementViewModel(BudgetStore store, AppSettings settings, IDialogService dialogs)
    {
        _store = store;
        _dialogs = dialogs;
        // Amounts being typed are always shown, even in privacy mode.
        _formatter = new MoneyFormatter(settings.Currency, culture: Localizer.Instance.Culture);
        _settings = settings;
        Currency = settings.Currency;
        Modes =
        [
            new SelectableOption(nameof(MovementMode.Deposit), 0, 3, SelectMode) { Label = Loc.Get("Action_Add"), IsSelected = true },
            new SelectableOption(nameof(MovementMode.Withdrawal), 1, 3, SelectMode) { Label = Loc.Get("Action_Withdraw") },
            new SelectableOption(nameof(MovementMode.Transfer), 2, 3, SelectMode) { Label = Loc.Get("Action_Transfer") },
        ];
        UpdateTexts();
    }

    public IReadOnlyList<SelectableOption> Modes { get; }

    public ObservableCollection<PocketChoice> SourceChoices { get; } = [];

    public ObservableCollection<PocketChoice> TargetChoices { get; } = [];

    public string Currency { get; }

    [ObservableProperty]
    private MovementMode _mode = MovementMode.Deposit;

    [ObservableProperty]
    private string _amountInput = string.Empty;

    [ObservableProperty]
    private string _amountDisplay = "0";

    [ObservableProperty]
    private bool _hasAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteDisplay), nameof(HasNote))]
    private string _note = string.Empty;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public string NoteDisplay => HasNote ? Note : Loc.Get("Movement_AddNote");

    /// <summary>Label of the keypad's decimal key: "," in French and Dutch, "." in English.</summary>
    public string DecimalSeparator => _formatter.DecimalSeparator;

    /// <summary>Whether the currency symbol goes before the typed amount ("€ 12" in Dutch and English).</summary>
    public bool SymbolBefore => _formatter.Split(0).Prefix.Length > 0;

    public bool SymbolAfter => !SymbolBefore;

    [ObservableProperty]
    private DateTime? _date = DateTime.Today;

    [ObservableProperty]
    private PocketChoice? _source;

    [ObservableProperty]
    private PocketChoice? _target;

    [ObservableProperty]
    private bool _isTransfer;

    [ObservableProperty]
    private string _sourceLabel = string.Empty;

    [ObservableProperty]
    private string _confirmText = string.Empty;

    [ObservableProperty]
    private Brush _accentBrush = Palette.HeroBrush(DefaultAccent);

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("pocketId", out var id) && int.TryParse(id?.ToString(), out var pocketId))
            _requestedPocketId = pocketId;
        if (query.TryGetValue("mode", out var mode) && Enum.TryParse<MovementMode>(mode?.ToString(), out var parsed))
            SetMode(parsed);
        _ = LoadAsync();
    }

    public Task LoadAsync() => LoadPocketsAsync();

    private async Task LoadPocketsAsync()
    {
        var formatter = _settings.Formatter;
        var summaries = await _store.GetPocketSummariesAsync();
        // The pocket we came from is listed first so it is visible without scrolling.
        _pockets = summaries
            .OrderByDescending(s => s.Pocket.Id == _requestedPocketId)
            .Select(s => new PocketChoice(s, formatter, SelectSource, SelectTarget))
            .ToList();

        SourceChoices.Clear();
        foreach (var pocket in _pockets)
            SourceChoices.Add(pocket);

        SelectSource(_pockets.FirstOrDefault(p => p.Id == _requestedPocketId) ?? _pockets.FirstOrDefault());
    }

    private void SelectMode(SelectableOption option) => SetMode(Enum.Parse<MovementMode>(option.Value));

    private void SelectSource(PocketChoice? choice)
    {
        Source = choice;
        foreach (var pocket in _pockets)
            pocket.IsSelected = pocket == choice;
        AccentBrush = Palette.HeroBrush(choice?.Color ?? DefaultAccent);
        RefreshTargets();
    }

    private void SelectTarget(PocketChoice? choice)
    {
        Target = choice;
        foreach (var pocket in TargetChoices)
            pocket.IsTargetSelected = pocket == choice;
    }

    [RelayCommand]
    private void Key(string key)
    {
        var input = AmountInput;
        var comma = input.IndexOf(',');
        switch (key)
        {
            case "back":
                input = input.Length > 0 ? input[..^1] : input;
                break;
            case ",":
                if (comma < 0)
                    input = (input.Length == 0 ? "0" : input) + ",";
                break;
            default:
                if (comma >= 0 && input.Length - comma > 2)
                    return;
                if (comma < 0 && input.Length >= MaxIntegerDigits)
                    return;
                input = input == "0" ? key : input + key;
                break;
        }

        AmountInput = input;
        HasError = false;
    }

    [RelayCommand]
    private async Task EditNoteAsync()
    {
        var note = await _dialogs.PromptAsync(Loc.Get("Movement_Note"), Loc.Get("Movement_NotePlaceholder"), Note, 60);
        if (note is not null)
            Note = note.Trim();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!AmountParser.TryParse(AmountInput, out var amount) || amount <= 0)
        {
            ShowError(Loc.Get("Movement_EnterAmount"));
            return;
        }
        if (Source is null)
        {
            ShowError(Loc.Get("Movement_ChoosePocket"));
            return;
        }
        if (Mode == MovementMode.Transfer && Target is null)
        {
            ShowError(Loc.Get("Movement_ChooseTarget"));
            return;
        }

        var date = MergeWithCurrentTime(Date ?? DateTime.Today);
        try
        {
            switch (Mode)
            {
                case MovementMode.Deposit:
                    await _store.AddMovementAsync(Source.Id, MovementKind.Deposit, amount, Note, date);
                    break;
                case MovementMode.Withdrawal:
                    await _store.AddMovementAsync(Source.Id, MovementKind.Withdrawal, amount, Note, date);
                    break;
                case MovementMode.Transfer:
                    await _store.TransferAsync(Source.Id, Target!.Id, amount, Note, date);
                    break;
            }
            Palette.Haptic();
            await Shell.Current.GoToAsync("..");
        }
        catch (BudgetException ex)
        {
            ShowError(Loc.Error(ex));
        }
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    partial void OnAmountInputChanged(string value)
    {
        HasAmount = AmountParser.TryParse(value, out var amount) && amount > 0;
        AmountDisplay = FormatInput(value);
        UpdateTexts();
    }

    partial void OnModeChanged(MovementMode value)
    {
        IsTransfer = value == MovementMode.Transfer;
        foreach (var option in Modes)
            option.IsSelected = option.Value == value.ToString();
        UpdateTexts();
        HasError = false;
    }

    private void SetMode(MovementMode mode) => Mode = mode;

    private void RefreshTargets()
    {
        TargetChoices.Clear();
        foreach (var pocket in _pockets.Where(p => p != Source))
            TargetChoices.Add(pocket);
        SelectTarget(TargetChoices.Contains(Target!) ? Target : TargetChoices.FirstOrDefault());
    }

    private void UpdateTexts()
    {
        var verb = Mode switch
        {
            MovementMode.Deposit => Loc.Get("Action_Add"),
            MovementMode.Withdrawal => Loc.Get("Action_Withdraw"),
            _ => Loc.Get("Action_Transfer"),
        };
        SourceLabel = Mode switch
        {
            MovementMode.Deposit => Loc.Get("Movement_Into"),
            MovementMode.Withdrawal => Loc.Get("Movement_OutOf"),
            _ => Loc.Get("Movement_From"),
        };
        ConfirmText = HasAmount && AmountParser.TryParse(AmountInput, out var amount)
            ? $"{verb} {_formatter.Format(amount)}"
            : verb;
    }

    /// <summary>"1234,5" → "1 234,5" (or "1,234.5" in English) while typing, keeping what was entered after the separator.</summary>
    private string FormatInput(string input)
    {
        if (input.Length == 0)
            return "0";
        var parts = input.Split(',');
        var whole = long.TryParse(parts[0], out var n) ? _formatter.FormatWhole(n) : parts[0];
        return parts.Length > 1 ? $"{whole}{_formatter.DecimalSeparator}{parts[1]}" : whole;
    }

    private static DateTime MergeWithCurrentTime(DateTime day) =>
        day.Date == DateTime.Today ? DateTime.Now : day.Date.AddHours(12);

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}

public sealed partial class PocketChoice(
    PocketSummary summary, MoneyFormatter formatter, Action<PocketChoice> onSelectSource, Action<PocketChoice> onSelectTarget)
    : ObservableObject
{
    public int Id { get; } = summary.Pocket.Id;

    public string Name { get; } = summary.Pocket.Name;

    public string Icon { get; } = summary.Pocket.Icon;

    public Color Color { get; } = Color.FromArgb(summary.Pocket.ColorHex);

    public string BalanceText { get; } = formatter.Format(summary.Balance);

    public Color SourceStroke => IsSelected ? Color : Colors.Transparent;

    public Color SourceBackground => IsSelected ? Palette.Soft(Color) : Colors.Transparent;

    public Color TargetStroke => IsTargetSelected ? Color : Colors.Transparent;

    public Color TargetBackground => IsTargetSelected ? Palette.Soft(Color) : Colors.Transparent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceStroke), nameof(SourceBackground))]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetStroke), nameof(TargetBackground))]
    private bool _isTargetSelected;

    [RelayCommand]
    private void SelectSource() => onSelectSource(this);

    [RelayCommand]
    private void SelectTarget() => onSelectTarget(this);
}
