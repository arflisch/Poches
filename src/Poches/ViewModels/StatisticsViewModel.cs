using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Pro;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>Twelve months of history and where the goals are heading (Poches Pro).</summary>
public sealed partial class StatisticsViewModel(BudgetStore store, AppSettings settings) : ReloadingViewModel
{
    public ObservableCollection<GoalItemViewModel> Goals { get; } = [];

    [ObservableProperty]
    private bool _hasActivity;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _averageText = string.Empty;

    [ObservableProperty]
    private string _depositsText = string.Empty;

    [ObservableProperty]
    private string _withdrawalsText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<double> _evolution = [];

    [ObservableProperty]
    private string _evolutionChange = string.Empty;

    [ObservableProperty]
    private string _firstMonth = string.Empty;

    [ObservableProperty]
    private string _lastMonth = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<double> _monthlyNet = [];

    [ObservableProperty]
    private IReadOnlyList<string> _monthLabels = [];

    [ObservableProperty]
    private bool _hasGoals;

    protected override async Task LoadCoreAsync()
    {
        var stats = await store.GetStatisticsAsync(DateTime.Today);
        var formatter = settings.Formatter;
        var culture = Localizer.Instance.Culture;

        HasActivity = stats.HasActivity;
        IsEmpty = !stats.HasActivity;
        AverageText = $"{formatter.FormatSigned(stats.AverageMonthlySavings)} {Loc.Get("Period_PerMonth")}";
        DepositsText = formatter.FormatSigned(stats.TotalDeposits);
        WithdrawalsText = formatter.FormatSigned(-stats.TotalWithdrawals);

        var months = stats.Months;
        Evolution = months.Select(m => (double)m.EndBalance).ToList();
        EvolutionChange = Loc.Format("Stats_Change", formatter.FormatSigned(months[^1].EndBalance - months[0].EndBalance), months.Count);
        FirstMonth = Loc.Date(months[0].Month, "MMM yyyy");
        LastMonth = Loc.Date(months[^1].Month, "MMM yyyy");
        MonthlyNet = months.Select(m => (double)m.Net).ToList();
        MonthLabels = months.Select(m => Loc.Capitalize(m.Month.ToString("MMM", culture))[..1]).ToList();

        Goals.Clear();
        foreach (var goal in stats.Goals)
            Goals.Add(new GoalItemViewModel(goal, formatter));
        HasGoals = Goals.Count > 0;
    }

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");
}

public sealed class GoalItemViewModel
{
    public GoalItemViewModel(GoalProjection goal, MoneyFormatter formatter)
    {
        Name = goal.Pocket.Name;
        Icon = goal.Pocket.Icon;
        Color = Color.FromArgb(goal.Pocket.ColorHex);
        SoftColor = Palette.Soft(Color);
        Progress = goal.Goal > 0 ? Math.Clamp((double)(goal.Balance / goal.Goal), 0, 1) : 0;
        AmountsText = $"{formatter.FormatShort(goal.Balance)} / {formatter.FormatShort(goal.Goal)}";
        StatusText = goal.IsReached
            ? Loc.Get("Detail_GoalReached")
            : goal.ReachedOn is { } month
                ? Loc.Format("Stats_GoalOn", month.ToString("MMMM yyyy", Localizer.Instance.Culture), formatter.FormatSigned(goal.MonthlyPace))
                : Loc.Get("Stats_GoalStalled");
    }

    public string Name { get; }

    public string Icon { get; }

    public Color Color { get; }

    public Color SoftColor { get; }

    public double Progress { get; }

    public string AmountsText { get; }

    public string StatusText { get; }
}
