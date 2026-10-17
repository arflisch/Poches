using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Controls;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Investments;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>The Investments tab (Poches Pro): value, performance by line and by purchase, allocation, evolution.</summary>
public sealed partial class InvestmentsViewModel(
    BudgetStore store,
    InvestmentService investments,
    AppSettings settings,
    ProService pro,
    IDialogService dialogs) : ReloadingViewModel, IBreakdown
{
    private bool _forceRefresh;

    public ObservableCollection<HoldingItemViewModel> Holdings { get; } = [];

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _allocationModes = [];

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _benchmarks = [];

    [ObservableProperty]
    private string _todayText = string.Empty;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _hasHoldings;

    [ObservableProperty]
    private string _privacyIcon = Icons.Eye;

    [ObservableProperty]
    private string _valuePrefix = string.Empty;

    [ObservableProperty]
    private string _valueWhole = "0";

    [ObservableProperty]
    private string _valueFraction = string.Empty;

    [ObservableProperty]
    private string _investedText = string.Empty;

    [ObservableProperty]
    private string _gainText = string.Empty;

    [ObservableProperty]
    private string _xirrText = string.Empty;

    [ObservableProperty]
    private bool _hasDayChange;

    [ObservableProperty]
    private string _dayText = string.Empty;

    [ObservableProperty]
    private bool _onlineQuotes;

    [ObservableProperty]
    private string _quotesText = string.Empty;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private IReadOnlyList<ChartSlice> _chartSlices = [];

    [ObservableProperty]
    private IReadOnlyList<LegendItem> _legend = [];

    [ObservableProperty]
    private string _chartCenterValue = "0";

    [ObservableProperty]
    private string _chartCenterLabel = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<EvolutionPoint> _evolution = [];

    [ObservableProperty]
    private bool _hasEvolution;

    [ObservableProperty]
    private string _evolutionText = string.Empty;

    [ObservableProperty]
    private bool _hasBenchmarkLegend;

    [ObservableProperty]
    private string _benchmarkLegend = string.Empty;

    /// <summary>Formats the evolution chart's axis.</summary>
    public Func<double, string> AxisFormatter => value => settings.Formatter.FormatShort(Math.Round((decimal)value));

    private string _allocationMode = "line";
    private InvestmentSnapshot? _snapshot;

    protected override async Task LoadCoreAsync()
    {
        TodayText = Loc.Date(DateTime.Today, "dddd d MMMM");
        IsUnlocked = pro.IsUnlocked;
        IsLocked = !IsUnlocked;
        if (IsLocked)
        {
            IsLoaded = true;
            return;
        }

        var formatter = settings.Formatter;
        PrivacyIcon = formatter.HideAmounts ? Icons.EyeOff : Icons.Eye;
        OnlineQuotes = settings.OnlineQuotes;

        var force = _forceRefresh;
        _forceRefresh = false;
        IsRefreshing = force;
        try
        {
            _snapshot = await investments.LoadAsync(force, CancellationToken.None);
        }
        finally
        {
            IsRefreshing = false;
        }

        var snapshot = _snapshot;
        var performance = snapshot.Performance;
        IsEmpty = performance.IsEmpty;
        HasHoldings = !IsEmpty;
        // Over a weekend or a holiday the latest prices are from an earlier session: say which day.
        var culture = Localizer.Instance.Culture;
        var sessionToday = snapshot.QuotesAt?.Date == DateTime.Today;
        QuotesText = !OnlineQuotes
            ? Loc.Get("Inv_QuotesOff")
            : snapshot.Quotes.Count == 0
                ? Loc.Get("Inv_QuotesNoSymbol")
            : snapshot.QuotesAt is { } at
                ? Loc.Format("Inv_QuotesAt", sessionToday ? at.ToString("t", culture) : $"{at.ToString("ddd", culture)} {at.ToString("t", culture)}")
                    + (snapshot.MissingQuotes > 0 ? " · " + Loc.Format("Inv_QuotesMissing", snapshot.MissingQuotes) : string.Empty)
                : Loc.Get("Inv_QuotesNone");

        (ValuePrefix, ValueWhole, ValueFraction) = formatter.Split(performance.Value);
        InvestedText = Loc.Format("Inv_InvestedSince", formatter.Format(performance.Invested));
        GainText = $"{formatter.FormatSigned(performance.Gain)} · {InvestmentText.SignedPercent(performance.Return)}";
        XirrText = performance.Xirr is { } xirr ? Loc.Format("Inv_XirrValue", InvestmentText.SignedPercent(xirr)) : Loc.Get("Inv_XirrTooEarly");
        HasDayChange = performance.DayChange is not null;
        DayText = performance.DayChange is { } day && performance.DayChangeRatio is { } ratio
            ? sessionToday
                ? Loc.Format("Inv_Today", formatter.FormatSigned(day), InvestmentText.SignedPercent(ratio))
                : Loc.Format("Inv_LastSession", formatter.FormatSigned(day), InvestmentText.SignedPercent(ratio))
            : string.Empty;

        Holdings.Clear();
        foreach (var holding in performance.Holdings)
            Holdings.Add(new HoldingItemViewModel(holding, formatter, OpenHoldingAsync));

        if (AllocationModes.Count == 0)
        {
            AllocationModes =
            [
                new("line", 0, 2, o => SelectAllocation(o.Value)) { Label = Loc.Get("Inv_ByLine") },
                new("type", 1, 2, o => SelectAllocation(o.Value)) { Label = Loc.Get("Inv_ByType") },
            ];
        }
        UpdateAllocation();
        Benchmarks = BuildBenchmarks();
        IsLoaded = true;

        await LoadEvolutionAsync(snapshot);
    }

    private async Task LoadEvolutionAsync(InvestmentSnapshot snapshot)
    {
        var points = await investments.LoadEvolutionAsync(snapshot, CancellationToken.None);
        if (!ReferenceEquals(snapshot, _snapshot))
            return;

        Evolution = points;
        HasEvolution = points.Count >= 2;
        if (!HasEvolution)
            return;

        var formatter = settings.Formatter;
        var last = points[^1];
        var benchmark = settings.Benchmark;
        HasBenchmarkLegend = last.Benchmark is not null;
        BenchmarkLegend = HasBenchmarkLegend ? Loc.Format("Inv_BenchmarkLegend", InvestmentService.BenchmarkName(benchmark)) : string.Empty;
        EvolutionText = last.Benchmark is { } bench && bench > 0
            ? Loc.Format("Inv_EvolutionVs", InvestmentService.BenchmarkName(benchmark), formatter.Format(bench),
                InvestmentText.SignedPercent((double)(last.Value / bench) - 1))
            : benchmark.Length == 0
                ? Loc.Get("Inv_EvolutionNoBenchmark")
                : settings.OnlineQuotes ? Loc.Get("Inv_EvolutionUnavailable") : Loc.Get("Inv_EvolutionOffline");
    }

    private IReadOnlyList<SelectableOption> BuildBenchmarks()
    {
        var current = settings.Benchmark;
        var options = new List<SelectableOption>
        {
            new(string.Empty, 0, 3, SelectBenchmark) { Label = Loc.Get("Inv_BenchmarkNone"), IsSelected = current.Length == 0 },
        };
        options.AddRange(InvestmentService.Benchmarks.Select((b, i) =>
            new SelectableOption(b.Symbol, i + 1, 3, SelectBenchmark) { Label = b.Name, IsSelected = b.Symbol == current }));
        return options;
    }

    private void SelectBenchmark(SelectableOption option)
    {
        if (option.Value == settings.Benchmark)
            return;
        settings.Benchmark = option.Value;
        Palette.Haptic();
    }

    private void UpdateAllocation()
    {
        var formatter = settings.Formatter;
        foreach (var mode in AllocationModes)
            mode.IsSelected = mode.Value == _allocationMode;
        var holdings = _snapshot?.Performance.Holdings ?? [];
        (ChartSlices, Legend) = _allocationMode == "type"
            ? Breakdown.Build(
                holdings.GroupBy(h => h.Holding.Type).Select(g => (HoldingTypes.Name(g.Key), HoldingTypes.Color(g.Key), g.Sum(h => h.Value))),
                formatter)
            : Breakdown.Build(holdings.Select(h => (h.Holding.Name, Color.FromArgb(h.Holding.ColorHex), h.Value)), formatter);
        ChartCenterValue = holdings.Count.ToString(Localizer.Instance.Culture);
        ChartCenterLabel = Loc.Noun(holdings.Count, "Holding");
    }

    private void SelectAllocation(string mode)
    {
        _allocationMode = mode;
        UpdateAllocation();
        Palette.Haptic();
    }

    private Task OpenHoldingAsync(int holdingId) => Shell.Current.GoToAsync($"{Routes.Holding}?id={holdingId}");

    [RelayCommand]
    private Task AddPurchaseAsync() => Shell.Current.GoToAsync(Routes.Purchase);

    [RelayCommand]
    private void Refresh()
    {
        _forceRefresh = true;
        RequestReload();
    }

    [RelayCommand]
    private async Task ToggleOnlineAsync()
    {
        if (!settings.OnlineQuotes)
        {
            var accepted = await dialogs.ConfirmAsync(Loc.Get("Inv_OnlineTitle"), Loc.Get("Inv_OnlineText"), Loc.Get("Inv_OnlineAccept"));
            if (!accepted)
                return;
        }
        _forceRefresh = true;
        settings.OnlineQuotes = !settings.OnlineQuotes;
        Palette.Haptic();
    }

    [RelayCommand]
    private Task OpenProAsync() => Shell.Current.GoToAsync(Routes.Pro);

    [RelayCommand]
    private Task OpenSettingsAsync() => Shell.Current.GoToAsync(Routes.Settings);

    [RelayCommand]
    private void TogglePrivacy()
    {
        settings.HideAmounts = !settings.HideAmounts;
        Palette.Haptic();
    }

    [RelayCommand]
    private async Task LoadSamplesAsync()
    {
        await store.SeedSampleInvestmentsAsync(DateTime.Today, new SampleInvestmentTexts(
            Loc.Get("Sample_World"), Loc.Get("Sample_Sp500"), Loc.Get("Sample_EuroFund"), Loc.Get("Sample_StockAccount"), Loc.Get("Sample_LifeInsurance")));
        Palette.Haptic();
    }
}

public sealed class HoldingItemViewModel
{
    public HoldingItemViewModel(HoldingPerformance performance, MoneyFormatter formatter, Func<int, Task> open)
    {
        var holding = performance.Holding;
        Id = holding.Id;
        Name = holding.Name;
        Icon = HoldingTypes.Icon(holding.Type);
        Color = Color.FromArgb(holding.ColorHex);
        SoftColor = Palette.Soft(Color);
        var parts = new List<string> { HoldingTypes.Name(holding.Type) };
        if (holding.Account is { } account)
            parts.Add(account);
        parts.Add(Loc.Format("Inv_Units", InvestmentText.Quantity(performance.Quantity)));
        Subtitle = string.Join(" · ", parts);
        ValueText = formatter.Format(performance.Value);
        GainText = $"{formatter.FormatSigned(performance.Gain)} · {InvestmentText.SignedPercent(performance.Return)}";
        IsGain = performance.Gain > 0.004m;
        IsLoss = performance.Gain < -0.004m;
        WeightText = InvestmentText.Percent(performance.Weight);
        OpenCommand = new AsyncRelayCommand(() => open(Id));
    }

    public int Id { get; }

    public string Name { get; }

    public string Icon { get; }

    public Color Color { get; }

    public Color SoftColor { get; }

    public string Subtitle { get; }

    public string ValueText { get; }

    public string GainText { get; }

    public bool IsGain { get; }

    public bool IsLoss { get; }

    public string WeightText { get; }

    public IAsyncRelayCommand OpenCommand { get; }
}
