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

public sealed partial class MainViewModel : ReloadingViewModel
{
    /// <summary>Beyond this many pockets, the smallest ones are grouped as "Autres" in the chart.</summary>
    private const int ChartSlicesMax = 5;

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly Color OthersColor = Color.FromArgb("#94A3B8");

    private readonly BudgetStore _store;
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;

    public MainViewModel(BudgetStore store, AppSettings settings, IDialogService dialogs)
    {
        _store = store;
        _settings = settings;
        _dialogs = dialogs;
        var today = DateTime.Now.ToString("dddd d MMMM", French);
        TodayText = char.ToUpper(today[0], French) + today[1..];
    }

    public ObservableCollection<PocketItemViewModel> Pockets { get; } = [];

    public string TodayText { get; }

    [ObservableProperty]
    private IReadOnlyList<ChartSlice> _chartSlices = [];

    [ObservableProperty]
    private IReadOnlyList<LegendItem> _legend = [];

    [ObservableProperty]
    private string _totalWhole = "0";

    [ObservableProperty]
    private string _totalFraction = string.Empty;

    [ObservableProperty]
    private string _monthDeltaText = string.Empty;

    [ObservableProperty]
    private bool _hasMonthDelta;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _hasPockets;

    [ObservableProperty]
    private bool _hasChart;

    [ObservableProperty]
    private string _pocketCountText = "0";

    [ObservableProperty]
    private string _pocketCountLabel = "poche";

    [ObservableProperty]
    private string _privacyIcon = Icons.Eye;

    protected override async Task LoadCoreAsync()
    {
        var overview = await _store.GetOverviewAsync(DateTime.Now);
        var formatter = _settings.Formatter;

        (TotalWhole, TotalFraction) = formatter.Split(overview.Total);
        HasMonthDelta = overview.MonthDeltaCents != 0 && !formatter.HideAmounts;
        MonthDeltaText = $"{formatter.FormatSigned(overview.MonthDelta)} ce mois-ci";
        PrivacyIcon = formatter.HideAmounts ? Icons.EyeOff : Icons.Eye;

        Pockets.Clear();
        foreach (var pocket in overview.Pockets)
            Pockets.Add(new PocketItemViewModel(pocket, overview.ShareOf(pocket), formatter, OpenPocketAsync));

        HasPockets = Pockets.Count > 0;
        IsEmpty = !HasPockets;
        PocketCountText = Pockets.Count.ToString(French);
        PocketCountLabel = Pockets.Count > 1 ? "poches" : "poche";
        BuildChart(overview);
        IsLoaded = true;
    }

    private void BuildChart(BudgetOverview overview)
    {
        var funded = Pockets.Where(p => p.Balance > 0).ToList();
        HasChart = funded.Count > 0;

        var shown = funded.Count > ChartSlicesMax ? funded.Take(ChartSlicesMax - 1).ToList() : funded;
        var others = funded.Skip(shown.Count).ToList();

        var slices = shown.Select(p => new ChartSlice((double)p.Balance, p.Color)).ToList();
        var legend = shown.Select(p => new LegendItem(p.Name, p.Color, p.ShareText)).ToList();
        if (others.Count > 0)
        {
            var othersTotal = others.Sum(p => p.Balance);
            slices.Add(new ChartSlice((double)othersTotal, OthersColor));
            legend.Add(new LegendItem(
                $"Autres ({others.Count})",
                OthersColor,
                MoneyFormatter.FormatPercent(overview.TotalCents > 0 ? (double)(othersTotal / overview.Total) : 0)));
        }

        ChartSlices = slices;
        Legend = legend;
    }

    private Task OpenPocketAsync(int pocketId) =>
        Shell.Current.GoToAsync($"{Routes.Pocket}?id={pocketId}");

    [RelayCommand]
    private Task AddPocketAsync() => Shell.Current.GoToAsync(Routes.EditPocket);

    [RelayCommand]
    private Task AddMovementAsync() =>
        Shell.Current.GoToAsync(HasPockets ? Routes.Movement : Routes.EditPocket);

    [RelayCommand]
    private void TogglePrivacy()
    {
        _settings.HideAmounts = !_settings.HideAmounts;
        Palette.Haptic();
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var current = _settings.Currency;
        var options = AppSettings.Currencies.Select(c => c == current ? $"{c}  ✓" : c).ToArray();
        var choice = await _dialogs.ChooseAsync("Devise d'affichage", null, options);
        if (choice is not null)
            _settings.Currency = choice.Replace("✓", string.Empty).Trim();
    }

    [RelayCommand]
    private async Task LoadSamplesAsync()
    {
        await _store.SeedSampleDataAsync(DateTime.Now);
        Palette.Haptic();
    }
}

public sealed record LegendItem(string Name, Color Color, string Percent);

public sealed class PocketItemViewModel
{
    public PocketItemViewModel(PocketSummary summary, double share, MoneyFormatter formatter, Func<int, Task> open)
    {
        var pocket = summary.Pocket;
        Id = pocket.Id;
        Name = pocket.Name;
        Icon = pocket.Icon;
        Color = Color.FromArgb(pocket.ColorHex);
        SoftColor = Palette.Soft(Color);
        Balance = summary.Balance;
        BalanceText = formatter.Format(summary.Balance);
        ShareText = MoneyFormatter.FormatPercent(share);
        HasGoal = summary.GoalProgress is not null;
        GoalProgress = summary.GoalProgress ?? 0;
        Caption = pocket.Goal is { } goal
            ? $"{MoneyFormatter.FormatPercent(GoalProgress)} de {formatter.FormatShort(goal)}"
            : $"{ShareText} du total";
        OpenCommand = new AsyncRelayCommand(() => open(Id));
    }

    public int Id { get; }

    public string Name { get; }

    public string Icon { get; }

    public Color Color { get; }

    public Color SoftColor { get; }

    public decimal Balance { get; }

    public string BalanceText { get; }

    public string ShareText { get; }

    public bool HasGoal { get; }

    public double GoalProgress { get; }

    public string Caption { get; }

    public IAsyncRelayCommand OpenCommand { get; }
}
