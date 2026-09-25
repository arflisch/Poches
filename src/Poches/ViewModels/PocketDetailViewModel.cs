using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Controls;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

public sealed partial class PocketDetailViewModel(BudgetStore store, AppSettings settings, IDialogService dialogs)
    : ReloadingViewModel, IQueryAttributable
{
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
    private string _balancePrefix = string.Empty;

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
        (BalancePrefix, BalanceWhole, BalanceFraction) = formatter.Split(summary.Balance);

        HasGoal = pocket.Goal is not null;
        GoalProgress = summary.GoalProgress ?? 0;
        if (pocket.Goal is { } goal)
        {
            GoalLabel = Loc.Format("Detail_Goal", formatter.FormatShort(goal));
            GoalPercentText = formatter.FormatPercent(GoalProgress);
            GoalStatusText = summary.Balance >= goal
                ? Loc.Get("Detail_GoalReached")
                : Loc.Format("Detail_GoalRemaining", formatter.FormatShort(goal - summary.Balance));
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
            var title = Loc.Date(month.Key, "MMMM yyyy");
            Groups.Add(new MovementGroup(
                title,
                month.Select(m => CreateItem(m, names, formatter))));
        }
    }

    private MovementItemViewModel CreateItem(Movement movement, Dictionary<int, string> names, MoneyFormatter formatter)
    {
        var counterpart = movement.CounterpartPocketId is { } id && names.TryGetValue(id, out var n) ? n : Loc.Get("Detail_DeletedPocket");
        var (kindLabel, iconData, color) = movement.Kind switch
        {
            MovementKind.Deposit => (Loc.Get("Kind_Deposit"), Icons.ArrowDown, Positive),
            MovementKind.Withdrawal => (Loc.Get("Kind_Withdrawal"), Icons.ArrowUp, Negative),
            MovementKind.TransferIn => (Loc.Format("Kind_From", counterpart), Icons.Transfer, Neutral),
            _ => (Loc.Format("Kind_To", counterpart), Icons.Transfer, Neutral),
        };

        var date = movement.Date.ToString("d MMM", Localizer.Instance.Culture);
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
        var choice = await dialogs.ChooseAsync($"{item.Title} · {item.AmountText}", Loc.Get("Detail_DeleteMovement"));
        if (choice is null)
            return;

        try
        {
            await store.DeleteMovementAsync(item.Id);
            Palette.Haptic();
        }
        catch (BudgetException ex)
        {
            await dialogs.AlertAsync(Loc.Get("Detail_DeleteFailed"), Loc.Error(ex));
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
            await dialogs.AlertAsync(Loc.Get("Detail_EmptyTitle"), Loc.Get("Detail_EmptyWithdraw"));
            return;
        }
        await OpenMovementAsync(MovementMode.Withdrawal);
    }

    [RelayCommand]
    private async Task TransferAsync()
    {
        if (_pocketCount < 2)
        {
            await dialogs.AlertAsync(Loc.Get("Detail_OnlyPocketTitle"), Loc.Get("Detail_OnlyPocketText"));
            return;
        }
        if (_balance <= 0)
        {
            await dialogs.AlertAsync(Loc.Get("Detail_EmptyTitle"), Loc.Get("Detail_EmptyTransfer"));
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
