using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Controls;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Services;

namespace Poches.ViewModels;

public sealed partial class PocketDetailViewModel(BudgetStore store, AppSettings settings, IDialogService dialogs)
    : ReloadingViewModel, IQueryAttributable
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly Color Positive = Color.FromArgb("#10B981");
    private static readonly Color Negative = Color.FromArgb("#F43F5E");
    private static readonly Color Neutral = Color.FromArgb("#6366F1");

    private int _pocketId;
    private decimal _balance;
    private int _pocketCount;

    public ObservableCollection<MovementGroup> Groups { get; } = [];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private Color _color = Neutral;

    [ObservableProperty]
    private Color _softColor = Palette.Soft(Neutral);

    [ObservableProperty]
    private Brush _heroBrush = Palette.HeroBrush(Neutral);

    [ObservableProperty]
    private string _balanceWhole = string.Empty;

    [ObservableProperty]
    private string _balanceFraction = string.Empty;

    [ObservableProperty]
    private bool _hasGoal;

    [ObservableProperty]
    private double _goalProgress;

    [ObservableProperty]
    private string _goalLabel = string.Empty;

    [ObservableProperty]
    private string _goalPercentText = string.Empty;

    [ObservableProperty]
    private string _goalStatusText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<double> _history = [];

    [ObservableProperty]
    private bool _hasHistory;

    [ObservableProperty]
    private bool _isHistoryEmpty;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && int.TryParse(id?.ToString(), out var pocketId))
        {
            _pocketId = pocketId;
            RequestReload();
        }
    }

    protected override async Task LoadCoreAsync()
    {
        if (_pocketId == 0)
            return;

        var summary = await store.GetPocketSummaryAsync(_pocketId);
        if (summary is null)
            return; // Deleted: the page is being closed.

        var formatter = settings.Formatter;
        var pocket = summary.Pocket;
        _balance = summary.Balance;

        Name = pocket.Name;
        Icon = pocket.Icon;
        Color = Color.FromArgb(pocket.ColorHex);
        SoftColor = Palette.Soft(Color);
        HeroBrush = Palette.HeroBrush(Color);
        (BalanceWhole, BalanceFraction) = formatter.Split(summary.Balance);

        HasGoal = pocket.Goal is not null;
        GoalProgress = summary.GoalProgress ?? 0;
        if (pocket.Goal is { } goal)
        {
            GoalLabel = $"Objectif {formatter.FormatShort(goal)}";
            GoalPercentText = MoneyFormatter.FormatPercent(GoalProgress);
            GoalStatusText = summary.Balance >= goal
                ? "Objectif atteint, bravo ! 🎉"
                : $"Encore {formatter.FormatShort(goal - summary.Balance)} à mettre de côté";
        }

        var pockets = await store.GetPocketSummariesAsync();
        _pocketCount = pockets.Count;
        var names = pockets.ToDictionary(p => p.Pocket.Id, p => p.Pocket.Name);

        var movements = await store.GetMovementsAsync(_pocketId);
        History = [0d, .. BudgetStore.BuildBalanceHistory(movements).Select(p => (double)p.Balance)];
        HasHistory = movements.Count > 0;
        IsHistoryEmpty = movements.Count == 0;

        Groups.Clear();
        foreach (var month in movements.GroupBy(m => new DateTime(m.Date.Year, m.Date.Month, 1)))
        {
            var title = month.Key.ToString("MMMM yyyy", French);
            Groups.Add(new MovementGroup(
                char.ToUpper(title[0], French) + title[1..],
                month.Select(m => CreateItem(m, names, formatter))));
        }
    }

    private MovementItemViewModel CreateItem(Movement movement, Dictionary<int, string> names, MoneyFormatter formatter)
    {
        var counterpart = movement.CounterpartPocketId is { } id && names.TryGetValue(id, out var n) ? n : "une poche supprimée";
        var (kindLabel, iconData, color) = movement.Kind switch
        {
            MovementKind.Deposit => ("Ajout", Icons.ArrowDown, Positive),
            MovementKind.Withdrawal => ("Retrait", Icons.ArrowUp, Negative),
            MovementKind.TransferIn => ($"Depuis {counterpart}", Icons.Transfer, Neutral),
            _ => ($"Vers {counterpart}", Icons.Transfer, Neutral),
        };

        var date = movement.Date.ToString("d MMM", French);
        var title = movement.Note ?? kindLabel;
        var subtitle = movement.Note is null ? date : $"{kindLabel} · {date}";

        return new MovementItemViewModel(
            movement.Id,
            title,
            subtitle,
            formatter.FormatSigned(movement.Amount),
            movement.AmountCents >= 0 ? Positive : Negative,
            iconData,
            color,
            Palette.Soft(color),
            OnMovementTappedAsync);
    }

    private async Task OnMovementTappedAsync(MovementItemViewModel item)
    {
        var choice = await dialogs.ChooseAsync($"{item.Title} · {item.AmountText}", "Supprimer ce mouvement");
        if (choice is null)
            return;

        try
        {
            await store.DeleteMovementAsync(item.Id);
            Palette.Haptic();
        }
        catch (BudgetException ex)
        {
            await dialogs.AlertAsync("Suppression impossible", ex.Message);
        }
    }

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private Task EditAsync() => Shell.Current.GoToAsync($"{Routes.EditPocket}?id={_pocketId}");

    [RelayCommand]
    private Task DepositAsync() => OpenMovementAsync(MovementMode.Deposit);

    [RelayCommand]
    private async Task WithdrawAsync()
    {
        if (_balance <= 0)
        {
            await dialogs.AlertAsync("Poche vide", "Il n'y a rien à retirer de cette poche pour l'instant.");
            return;
        }
        await OpenMovementAsync(MovementMode.Withdrawal);
    }

    [RelayCommand]
    private async Task TransferAsync()
    {
        if (_pocketCount < 2)
        {
            await dialogs.AlertAsync("Une seule poche", "Crée une autre poche pour pouvoir y transférer de l'argent.");
            return;
        }
        if (_balance <= 0)
        {
            await dialogs.AlertAsync("Poche vide", "Ajoute d'abord de l'argent dans cette poche pour pouvoir le transférer.");
            return;
        }
        await OpenMovementAsync(MovementMode.Transfer);
    }

    private Task OpenMovementAsync(MovementMode mode) =>
        Shell.Current.GoToAsync($"{Routes.Movement}?pocketId={_pocketId}&mode={mode}");
}

public sealed class MovementGroup(string title, IEnumerable<MovementItemViewModel> items)
    : List<MovementItemViewModel>(items)
{
    public string Title { get; } = title;
}

public sealed class MovementItemViewModel
{
    public MovementItemViewModel(
        int id, string title, string subtitle, string amountText, Color amountColor,
        string iconData, Color iconColor, Color iconBackground, Func<MovementItemViewModel, Task> onTapped)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
        AmountText = amountText;
        AmountColor = amountColor;
        IconData = iconData;
        IconColor = iconColor;
        IconBackground = iconBackground;
        TapCommand = new AsyncRelayCommand(() => onTapped(this));
    }

    public int Id { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string AmountText { get; }

    public Color AmountColor { get; }

    public string IconData { get; }

    public Color IconColor { get; }

    public Color IconBackground { get; }

    public IAsyncRelayCommand TapCommand { get; }
}
