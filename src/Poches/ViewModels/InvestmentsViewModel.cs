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

/// <summary>
/// The Investments tab (Poches Pro): value, performance by line and by purchase, realized gains and dividends,
/// allocation, evolution and returns by period, the goal and its projection, the Belgian tax summary and what the
/// money is really in.
/// </summary>
public sealed partial class InvestmentsViewModel(
    BudgetStore store,
    InvestmentService investments,
    AppSettings settings,
    ProService pro,
    IDialogService dialogs) : ReloadingViewModel, IBreakdown
{
    private bool _forceRefresh;

    public ObservableCollection<HoldingItemViewModel> Holdings { get; } = [];

    public ObservableCollection<HoldingItemViewModel> ClosedHoldings { get; } = [];

    [ObservableProperty]
    private bool _hasClosed;

    [ObservableProperty]
    private bool _hasExtras;

    [ObservableProperty]
    private string _extrasText = string.Empty;

    [ObservableProperty]
    private bool _hasDetected;

    [ObservableProperty]
    private string _detectedText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _periodOptions = [];

    [ObservableProperty]
    private bool _hasPeriods;

    [ObservableProperty]
    private string _periodReturn = string.Empty;

    [ObservableProperty]
    private bool _periodIsGain;

    [ObservableProperty]
    private bool _periodIsLoss;

    [ObservableProperty]
    private string _periodDetail = string.Empty;

    [ObservableProperty]
    private string _periodBenchmark = string.Empty;

    [ObservableProperty]
    private bool _hasPeriodBenchmark;

    [ObservableProperty]
    private string _planText = string.Empty;

    [ObservableProperty]
    private bool _hasGoal;

    [ObservableProperty]
    private string _goalText = string.Empty;

    [ObservableProperty]
    private double _goalProgress;

    [ObservableProperty]
    private string _goalProgressText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ProjectionBand>? _projection;

    [ObservableProperty]
    private double _goalValue;

    [ObservableProperty]
    private bool _hasProjection;

    [ObservableProperty]
    private string _projectionText = string.Empty;

    [ObservableProperty]
    private string _probabilityText = string.Empty;

    [ObservableProperty]
    private bool _hasProbability;

    [ObservableProperty]
    private bool _hasTargets;

    [ObservableProperty]
    private string _driftText = string.Empty;

    [ObservableProperty]
    private bool _showTax;

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _taxYears = [];

    [ObservableProperty]
    private IReadOnlyList<SummaryRow> _taxRows = [];

    [ObservableProperty]
    private string _taxNote = string.Empty;

    [ObservableProperty]
    private bool _hasExposure;

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _exposureModes = [];

    [ObservableProperty]
    private IReadOnlyList<ExposureRow> _exposureRows = [];

    [ObservableProperty]
    private string _terText = string.Empty;

    [ObservableProperty]
    private string _exposureNote = string.Empty;

    [ObservableProperty]
    private bool _hasExposureNote;

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
    private string _periodKey = "1y";
    private string _exposureMode = "sector";
    private int _taxYear = DateTime.Today.Year;
    private IReadOnlyList<PeriodPerformance> _periods = [];
    private ExposureReport? _exposure;
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
        ClosedHoldings.Clear();
        foreach (var holding in performance.Holdings)
            (holding.Closed ? ClosedHoldings : Holdings).Add(new HoldingItemViewModel(holding, formatter, OpenHoldingAsync));
        HasClosed = ClosedHoldings.Count > 0;

        var extras = new List<string>();
        if (performance.Realized != 0)
            extras.Add(Loc.Format("Inv_RealizedValue", formatter.FormatSigned(performance.Realized)));
        if (performance.Dividends != 0)
            extras.Add(Loc.Format("Inv_DividendsValue", formatter.Format(performance.Dividends)));
        if (performance.Reinvested > 0)
            extras.Add(Loc.Format("Inv_ReinvestedValue", formatter.Format(performance.Reinvested)));
        HasExtras = extras.Count > 0;
        ExtrasText = string.Join(" · ", extras);

        ShowPlan(snapshot);
        ShowTax = snapshot.BelgianTax && !IsEmpty;
        if (ShowTax)
            ShowTaxYear(snapshot);

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
        await LoadExposureAsync(snapshot);
        await LoadDetectedAsync(snapshot);
    }

    private async Task LoadEvolutionAsync(InvestmentSnapshot snapshot)
    {
        var result = await investments.LoadEvolutionAsync(snapshot, CancellationToken.None);
        if (!ReferenceEquals(snapshot, _snapshot))
            return;

        var points = result.Points;
        Evolution = points;
        HasEvolution = points.Count >= 2;
        _periods = result.Periods;
        HasPeriods = _periods.Count > 0;
        if (HasPeriods)
            ShowPeriods();
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
        var holdings = _snapshot?.Performance.Open.ToList() ?? [];
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

    private void ShowPeriods()
    {
        if (!_periods.Any(p => p.Key == _periodKey))
            _periodKey = _periods.Any(p => p.Key == "1y") ? "1y" : _periods[^1].Key;
        PeriodOptions = _periods.Select((p, i) => new SelectableOption(p.Key, i, _periods.Count, o =>
        {
            _periodKey = o.Value;
            ShowPeriods();
            Palette.Haptic();
        })
        {
            Label = PeriodName(p.Key),
            IsSelected = p.Key == _periodKey,
        }).ToList();

        var period = _periods.First(p => p.Key == _periodKey);
        var formatter = settings.Formatter;
        PeriodReturn = InvestmentText.SignedPercent(period.Twr);
        PeriodIsGain = period.Twr > 0.0005;
        PeriodIsLoss = period.Twr < -0.0005;
        PeriodDetail = (period.Annualized is { } yearly ? Loc.Format("Inv_PerYear", InvestmentText.SignedPercent(yearly)) + " · " : string.Empty)
            + Loc.Format("Inv_PeriodGain", formatter.FormatSigned(period.Gain), Loc.Date(period.From, "d MMM yyyy"));
        HasPeriodBenchmark = period.Benchmark is not null && settings.Benchmark.Length > 0;
        PeriodBenchmark = period.Benchmark is { } bench
            ? Loc.Format("Inv_PeriodBenchmark", InvestmentService.BenchmarkName(settings.Benchmark), InvestmentText.SignedPercent(bench))
            : string.Empty;
    }

    private static string PeriodName(string key) => key switch
    {
        "ytd" => Loc.Get("Inv_PeriodYtd"),
        "1y" => Loc.Get("Inv_Period1y"),
        "3y" => Loc.Get("Inv_Period3y"),
        "5y" => Loc.Get("Inv_Period5y"),
        _ => Loc.Get("Inv_PeriodAll"),
    };

    /// <summary>The monthly plan, the goal and the simulated paths towards it.</summary>
    private void ShowPlan(InvestmentSnapshot snapshot)
    {
        var formatter = settings.Formatter;
        var plan = snapshot.Plan;
        var value = snapshot.Performance.Value;
        PlanText = plan.Monthly is { } monthly
            ? Loc.Format("Inv_PlanMonthly", formatter.Format(monthly), plan.MonthlyDay)
            : Loc.Get("Inv_PlanNone");
        HasGoal = plan.Goal is not null && plan.GoalDate is not null;
        GoalText = plan.Goal is { } goal && plan.GoalDate is { } date
            ? Loc.Format("Inv_GoalValue", formatter.Format(goal), Loc.Date(date, "MMMM yyyy"))
            : Loc.Get("Inv_GoalNone");
        GoalProgress = plan.Goal is { } g && g > 0 ? Math.Clamp((double)(value / g), 0, 1) : 0;
        GoalProgressText = HasGoal ? Loc.Format("Inv_GoalProgress", InvestmentText.Percent(GoalProgress)) : string.Empty;

        var open = snapshot.Performance.Open.ToList();
        var withTarget = open.Where(l => l.Holding.Target is > 0).ToList();
        HasTargets = withTarget.Count > 0;
        if (HasTargets)
        {
            var total = open.Sum(l => l.Value);
            var drift = withTarget.Select(l => (l.Holding.Name, Drift: (total > 0 ? (double)(l.Value / total) : 0) - (double)l.Holding.Target!.Value))
                .MaxBy(x => Math.Abs(x.Drift));
            DriftText = Loc.Format("Inv_Drift", drift.Name, InvestmentText.SignedPercent(drift.Drift));
        }

        HasProjection = plan.Monthly is not null || HasGoal;
        if (!HasProjection)
            return;
        var months = plan.GoalDate is { } until
            ? Math.Max(1, (until.Year - DateTime.Today.Year) * 12 + until.Month - DateTime.Today.Month)
            : 120;
        var monthlyAmount = (double)(plan.Monthly ?? 0);
        var goalAmount = (double)(plan.Goal ?? 0);
        GoalValue = HasGoal ? goalAmount : 0;
        _ = ProjectAsync(snapshot, (double)value, monthlyAmount, months, (double)plan.ExpectedReturn, (double)plan.Volatility, goalAmount);
    }

    private async Task ProjectAsync(InvestmentSnapshot snapshot, double value, double monthly, int months, double expected, double volatility, double goal)
    {
        // Thousands of simulated paths: off the UI thread.
        var (projection, required) = await Task.Run(() => (
            Planning.Project(value, monthly, months, expected, volatility, goal),
            goal > 0 ? Planning.RequiredMonthly(value, goal, months, expected, volatility) : null));
        if (!ReferenceEquals(snapshot, _snapshot))
            return;
        var formatter = settings.Formatter;
        Projection = projection.Bands;
        var end = projection.Bands[^1];
        var year = DateTime.Today.AddMonths(months).Year;
        ProjectionText = Loc.Format("Inv_ProjectionMedian", year, formatter.Format(Math.Round((decimal)end.P50)),
            formatter.Format(Math.Round((decimal)end.P10)), formatter.Format(Math.Round((decimal)end.P90)));
        HasProbability = projection.Probability is not null;
        ProbabilityText = projection.Probability is { } probability
            ? Loc.Format("Inv_Probability", InvestmentText.Percent(probability, 0))
                + (required is > 0 && probability < 0.5 ? " · " + Loc.Format("Inv_RequiredMonthly", formatter.Format(Math.Ceiling((decimal)required.Value))) : string.Empty)
            : string.Empty;
    }

    private void ShowTaxYear(InvestmentSnapshot snapshot)
    {
        var years = snapshot.Purchases.Select(p => p.Date.Year)
            .Concat(snapshot.Sales.Select(s => s.Date.Year))
            .Concat(snapshot.Dividends.Select(d => d.Date.Year))
            .Append(DateTime.Today.Year)
            .Distinct()
            .OrderDescending()
            .Take(4)
            .ToList();
        if (!years.Contains(_taxYear))
            _taxYear = years[0];
        TaxYears = years.Select((y, i) => new SelectableOption(y.ToString(System.Globalization.CultureInfo.InvariantCulture), i, years.Count, o =>
        {
            _taxYear = int.Parse(o.Value, System.Globalization.CultureInfo.InvariantCulture);
            if (_snapshot is { } current)
                ShowTaxYear(current);
            Palette.Haptic();
        })
        {
            IsSelected = y == _taxYear,
        }).ToList();

        var report = investments.Tax(snapshot, _taxYear);
        var formatter = settings.Formatter;
        var rows = new List<SummaryRow>();
        if (report.Applies)
        {
            rows.Add(new SummaryRow(Loc.Get("Tax_NetGain"), formatter.FormatSigned(report.NetGain)));
            rows.Add(new SummaryRow(Loc.Get("Tax_ExemptionLeft"), formatter.Format(report.ExemptionLeft)));
            rows.Add(new SummaryRow(Loc.Format("Tax_Estimated", InvestmentText.Percent((double)report.Rate, 0)), formatter.Format(report.EstimatedTax), true));
            if (report.Year == DateTime.Today.Year)
            {
                rows.Add(new SummaryRow(Loc.Get("Tax_Latent"), formatter.FormatSigned(report.LatentGain)));
                rows.Add(new SummaryRow(Loc.Get("Tax_IfSoldToday"), formatter.Format(report.TaxIfSoldToday)));
            }
        }
        rows.Add(new SummaryRow(Loc.Get("Tax_TobPaid"), formatter.Format(report.TobPaid)));
        if (report.Dividends.Count > 0)
        {
            rows.Add(new SummaryRow(Loc.Get("Tax_DividendsGross"), formatter.Format(report.Dividends.Sum(d => d.Gross))));
            rows.Add(new SummaryRow(Loc.Get("Tax_Withheld"), formatter.Format(report.Dividends.Sum(d => d.Withholding))));
        }
        TaxRows = rows;

        var notes = new List<string>();
        if (!report.Applies)
            notes.Add(Loc.Get("Tax_NotYet"));
        if (report.MissingReference.Count > 0)
            notes.Add(Loc.Format("Tax_MissingReference", string.Join(", ", report.MissingReference)));
        if (report.ReyndersLines.Count > 0)
            notes.Add(Loc.Format("Tax_Reynders", string.Join(", ", report.ReyndersLines)));
        if (_exposure is { ReyndersHints.Count: > 0 } exposure)
            notes.Add(Loc.Format("Tax_ReyndersHint", string.Join(", ", exposure.ReyndersHints)));
        notes.Add(Loc.Get("Tax_Disclaimer"));
        TaxNote = string.Join("\n\n", notes);
    }

    private async Task LoadExposureAsync(InvestmentSnapshot snapshot)
    {
        ExposureReport? exposure = null;
        try
        {
            exposure = await investments.LoadExposureAsync(snapshot, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Only an analysis: the tab works without it.
        }
        if (!ReferenceEquals(snapshot, _snapshot))
            return;
        _exposure = exposure;
        HasExposure = exposure is { Value: > 0 };
        if (HasExposure)
            ShowExposure();
        if (ShowTax && exposure is { ReyndersHints.Count: > 0 })
            ShowTaxYear(snapshot);
    }

    private void ShowExposure()
    {
        if (_exposure is not { } exposure)
            return;
        string[] modes = ["sector", "class", "currency", "country"];
        ExposureModes = modes.Select((m, i) => new SelectableOption(m, i, modes.Length, o =>
        {
            _exposureMode = o.Value;
            ShowExposure();
            Palette.Haptic();
        })
        {
            Label = m switch
            {
                "sector" => Loc.Get("Exposure_BySector"),
                "class" => Loc.Get("Exposure_ByClass"),
                "currency" => Loc.Get("Exposure_ByCurrency"),
                _ => Loc.Get("Exposure_ByCountry"),
            },
            IsSelected = m == _exposureMode,
        }).ToList();

        var slices = _exposureMode switch
        {
            "class" => exposure.ByAssetClass,
            "currency" => exposure.ByCurrency,
            "country" => exposure.ByCountry,
            _ => exposure.BySector,
        };
        var colors = Palette.Colors;
        var shown = slices.Take(8).ToList();
        var rest = slices.Skip(shown.Count).Sum(s => s.Weight);
        var rows = shown.Select((s, i) => new ExposureRow(
            _exposureMode == "currency" && s.Key != ExposureReport.Unlisted ? s.Key : HoldingTypes.ExposureName(s.Key),
            InvestmentText.Percent(s.Weight),
            s.Weight,
            s.Key is ExposureReport.Unknown or ExposureReport.Unlisted ? Color.FromArgb("#94A3B8") : Color.FromArgb(colors[i % colors.Count]))).ToList();
        if (rest > 0)
            rows.Add(new ExposureRow(Loc.Format("Main_Others", slices.Count - shown.Count), InvestmentText.Percent(rest), rest, Color.FromArgb("#94A3B8")));
        ExposureRows = rows;

        var formatter = settings.Formatter;
        TerText = exposure.WeightedTer is { } ter
            ? Loc.Format("Exposure_Ter", InvestmentText.Percent((double)ter, 2), formatter.Format(exposure.AnnualCost))
            : Loc.Get("Exposure_TerUnknown");
        var notes = new List<string>();
        if (exposure.Ter.Where(t => t.Ter is null).Select(t => t.Line).ToList() is { Count: > 0 } unknown)
            notes.Add(Loc.Format("Exposure_TerMissing", string.Join(", ", unknown)));
        if (_exposureMode == "sector" && exposure.Coverage < 0.995)
            notes.Add(Loc.Format("Exposure_Coverage", InvestmentText.Percent(exposure.Coverage, 0)));
        HasExposureNote = notes.Count > 0;
        ExposureNote = string.Join(" ", notes);
    }

    private async Task LoadDetectedAsync(InvestmentSnapshot snapshot)
    {
        IReadOnlyList<DetectedDividend> detected = [];
        try
        {
            detected = await investments.DetectDividendsAsync(snapshot, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Only a suggestion.
        }
        if (!ReferenceEquals(snapshot, _snapshot))
            return;
        HasDetected = detected.Count > 0;
        if (!HasDetected)
            return;
        var names = detected.Select(d => d.Line).Distinct().ToList();
        DetectedText = Loc.Format("Inv_DetectedBanner", detected.Count, Loc.Noun(detected.Count, "Dividend"),
            string.Join(", ", names.Take(3)) + (names.Count > 3 ? "…" : string.Empty), settings.Formatter.Format(detected.Sum(d => d.GrossBase)));
    }

    private Task OpenHoldingAsync(int holdingId) => Shell.Current.GoToAsync($"{Routes.Holding}?id={holdingId}");

    [RelayCommand]
    private Task AddPurchaseAsync() => Shell.Current.GoToAsync(Routes.Purchase);

    /// <summary>The floating button: a purchase, a sale, a dividend or a whole bank statement.</summary>
    [RelayCommand]
    private async Task AddOperationAsync()
    {
        var purchase = Loc.Get("Inv_AddPurchase");
        var sale = Loc.Get("Inv_AddSale");
        var dividend = Loc.Get("Inv_AddDividend");
        var import = Loc.Get("Csv_Title");
        var options = Holdings.Count > 0 ? new[] { purchase, sale, dividend, import } : [purchase, import];
        var choice = await dialogs.ChooseAsync(Loc.Get("Inv_AddWhat"), null, options);
        if (choice == purchase)
            await Shell.Current.GoToAsync(Routes.Purchase);
        else if (choice == sale)
            await Shell.Current.GoToAsync($"{Routes.Operation}?kind={OperationEditViewModel.SaleKind}");
        else if (choice == dividend)
            await Shell.Current.GoToAsync($"{Routes.Operation}?kind={OperationEditViewModel.DividendKind}");
        else if (choice == import)
            await Shell.Current.GoToAsync(Routes.CsvImport);
    }

    [RelayCommand]
    private Task ImportCsvAsync() => Shell.Current.GoToAsync(Routes.CsvImport);

    [RelayCommand]
    private Task OpenDetectedAsync() => Shell.Current.GoToAsync(Routes.DetectedDividends);

    [RelayCommand]
    private Task EditPlanAsync() => Shell.Current.GoToAsync(Routes.InvestmentPlan);

    [RelayCommand]
    private Task OpenContributionAsync() => Shell.Current.GoToAsync(Routes.Contribution);

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
        IsClosed = performance.Closed;
        Name = holding.Name;
        Icon = HoldingTypes.Icon(holding.Type);
        Color = Color.FromArgb(holding.ColorHex);
        SoftColor = Palette.Soft(Color);
        var parts = new List<string> { HoldingTypes.Name(holding.Type) };
        if (holding.Account is { } account)
            parts.Add(account);
        if (IsClosed)
        {
            // Sold out: what it earned in all, realized gains and dividends.
            parts.Add(Loc.Get("Inv_ClosedLine"));
            Subtitle = string.Join(" · ", parts);
            var earned = performance.Realized + performance.Dividends;
            ValueText = formatter.FormatSigned(earned);
            GainText = performance.Dividends != 0
                ? Loc.Format("Inv_ClosedDetail", formatter.FormatSigned(performance.Realized), formatter.Format(performance.Dividends))
                : Loc.Get("Inv_Realized");
            IsGain = earned > 0.004m;
            IsLoss = earned < -0.004m;
            WeightText = string.Empty;
        }
        else
        {
            parts.Add(Loc.Format("Inv_Units", InvestmentText.Quantity(performance.Quantity)));
            Subtitle = string.Join(" · ", parts);
            ValueText = formatter.Format(performance.Value);
            GainText = $"{formatter.FormatSigned(performance.Gain)} · {InvestmentText.SignedPercent(performance.Return)}";
            IsGain = performance.Gain > 0.004m;
            IsLoss = performance.Gain < -0.004m;
            WeightText = InvestmentText.Percent(performance.Weight);
        }
        OpenCommand = new AsyncRelayCommand(() => open(Id));
    }

    public int Id { get; }

    public bool IsClosed { get; }

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

/// <summary>A label and its amount in a summary card.</summary>
public sealed record SummaryRow(string Label, string Value, bool Emphasis = false);

/// <summary>A share of the exposure analysis, with its bar.</summary>
public sealed record ExposureRow(string Name, string PercentText, double Weight, Color Color);
