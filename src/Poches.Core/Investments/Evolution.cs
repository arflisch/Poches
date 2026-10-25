using Poches.Core.Models;

namespace Poches.Core.Investments;

/// <param name="Invested">Cost of the units held on that date.</param>
/// <param name="Benchmark">What the same cash would be worth had each purchase bought the benchmark instead (and each sale or dividend sold it).</param>
/// <param name="Flow">Money in since the previous point: purchases, less sale proceeds and dividends received.</param>
public sealed record EvolutionPoint(DateTime Date, decimal Invested, decimal Value, decimal? Benchmark, decimal Flow = 0);

/// <summary>Time-weighted return over a period; <see cref="Benchmark"/> is the index's change over the same dates.</summary>
public sealed record PeriodPerformance(string Key, DateTime From, DateTime To, double Twr, double? Annualized, decimal Gain, double? Benchmark);

/// <summary>Portfolio value, money invested and benchmark value from the first purchase to today. Pure functions.</summary>
public static class Evolution
{
    /// <param name="historyByHolding">Daily closes in the portfolio currency, oldest first, by holding id.</param>
    /// <param name="latestByHolding">Today's price by holding id (quote or manual price).</param>
    /// <param name="benchmark">Daily closes of the benchmark in the portfolio currency, oldest first.</param>
    public static IReadOnlyList<EvolutionPoint> Compute(
        IReadOnlyList<Holding> holdings,
        IReadOnlyList<Purchase> purchases,
        IReadOnlyDictionary<int, IReadOnlyList<DailyClose>> historyByHolding,
        IReadOnlyDictionary<int, decimal> latestByHolding,
        IReadOnlyList<DailyClose>? benchmark,
        decimal? benchmarkLatest,
        DateTime today,
        IReadOnlyList<Sale>? sales = null,
        IReadOnlyList<Dividend>? dividends = null)
    {
        var known = holdings.ToDictionary(h => h.Id);
        var lots = purchases.Where(p => known.ContainsKey(p.HoldingId)).OrderBy(p => p.Date).ThenBy(p => p.Id).ToList();
        if (lots.Count == 0)
            return [];
        var sold = (sales ?? []).Where(s => known.ContainsKey(s.HoldingId)).OrderBy(s => s.Date).ToList();
        var received = (dividends ?? []).Where(d => known.ContainsKey(d.HoldingId)).ToList();
        var slices = lots.GroupBy(l => l.HoldingId)
            .SelectMany(g => InvestmentMath.MatchSales(g.ToList(), sold.Where(s => s.HoldingId == g.Key).ToList(), null))
            .ToList();

        var first = lots[0].Date.Date;
        var last = today.Date > lots[^1].Date.Date ? today.Date : lots[^1].Date.Date;
        // Daily up to ~13 months, weekly beyond: enough points for a phone-wide chart.
        var step = (last - first).TotalDays <= 400 ? 1 : 7;
        var dates = new List<DateTime>();
        for (var d = first; d < last; d = d.AddDays(step))
            dates.Add(d);
        dates.Add(last);

        // Without market history (manual lines, or before a symbol's history starts) the price follows the
        // known points: each purchase and sale price, then the manual price.
        var fallback = known.Values.ToDictionary(h => h.Id, h =>
        {
            var points = lots.Where(l => l.HoldingId == h.Id).Select(l => new DailyClose(l.Date.Date, l.BaseUnitPrice))
                .Concat(sold.Where(s => s.HoldingId == h.Id).Select(s => new DailyClose(s.Date.Date, s.BaseUnitPrice)))
                .ToList();
            if (h.ManualPrice is { } manual)
                points.Add(new DailyClose((h.ManualPriceDate ?? last).Date, manual));
            return (IReadOnlyList<DailyClose>)points.OrderBy(p => p.Date).ToList();
        });

        var bench = benchmark is { Count: > 0 } b ? b : null;
        decimal BenchUnits(DateTime date, decimal amount) => bench?.CloseNear(date.Date) is { } p and > 0 ? amount / p : 0m;

        var points = new List<EvolutionPoint>(dates.Count);
        var previous = DateTime.MinValue;
        foreach (var date in dates)
        {
            decimal value = 0;
            foreach (var group in lots.Where(l => l.Date.Date <= date).GroupBy(l => l.HoldingId))
            {
                var held = group.Sum(l => l.Quantity) - sold.Where(s => s.HoldingId == group.Key && s.Date.Date <= date).Sum(s => s.Quantity);
                if (held > 0)
                    value += held * PriceAt(group.Key, date);
            }
            var invested = lots.Where(l => l.Date.Date <= date).Sum(l => l.Invested) - slices.Where(s => s.Sale.Date.Date <= date).Sum(s => s.Cost);

            decimal? benchValue = null;
            if (bench is not null)
            {
                var units = lots.Where(l => l.Date.Date <= date).Sum(l => BenchUnits(l.Date, l.Invested))
                    - sold.Where(s => s.Date.Date <= date).Sum(s => BenchUnits(s.Date, s.Proceeds))
                    - received.Where(d => d.Date.Date <= date).Sum(d => BenchUnits(d.Date, d.Net));
                if ((date == last && benchmarkLatest is { } latest ? latest : bench.CloseNear(date)) is { } bp)
                    benchValue = Math.Round(Math.Max(0, units) * bp, 2);
            }

            var flow = lots.Where(l => l.Date.Date > previous && l.Date.Date <= date).Sum(l => l.Invested)
                - sold.Where(s => s.Date.Date > previous && s.Date.Date <= date).Sum(s => s.Proceeds)
                - received.Where(d => d.Date.Date > previous && d.Date.Date <= date).Sum(d => d.Net);
            points.Add(new EvolutionPoint(date, Math.Round(invested, 2), Math.Round(value, 2), benchValue, Math.Round(flow, 2)));
            previous = date;
        }
        return points;

        decimal PriceAt(int holdingId, DateTime date)
        {
            if (date == last && latestByHolding.TryGetValue(holdingId, out var latest))
                return latest;
            if (historyByHolding.TryGetValue(holdingId, out var history) && history.CloseOnOrBefore(date) is { } close)
                return close;
            return fallback[holdingId].CloseNear(date) ?? 0;
        }
    }

    /// <summary>
    /// Time-weighted return year to date, over 1, 3 and 5 years when the history covers them, and since the first
    /// purchase: r = (V_t − flow_t) / V_t−1 chained, so contributions don't count as performance.
    /// </summary>
    public static IReadOnlyList<PeriodPerformance> Periods(IReadOnlyList<EvolutionPoint> points, IReadOnlyList<DailyClose>? benchmark, DateTime today)
    {
        if (points.Count < 2)
            return [];
        var first = points[0].Date;
        var starts = new List<(string Key, DateTime From)> { ("ytd", new DateTime(today.Year, 1, 1).AddDays(-1)) };
        foreach (var years in new[] { 1, 3, 5 })
            if (today.AddYears(-years) > first) starts.Add(($"{years}y", today.AddYears(-years)));
        starts.Add(("all", first));

        var result = new List<PeriodPerformance>();
        foreach (var (key, from) in starts)
        {
            // Start at the last point on or before the period start (or the first point when the history is younger).
            var startIndex = points.Select((p, i) => (p, i)).LastOrDefault(x => x.p.Date <= from).i;
            if (key == "ytd" && points[0].Date > from) startIndex = 0;
            var window = points.Skip(startIndex).ToList();
            if (window.Count < 2)
                continue;

            double growth = 1;
            decimal flows = 0;
            for (var i = 1; i < window.Count; i++)
            {
                var before = window[i - 1].Value;
                flows += window[i].Flow;
                if (before > 0)
                    growth *= (double)((window[i].Value - window[i].Flow) / before);
            }
            // From the first purchase, the starting value is that purchase: money in, not a gain.
            var fromStart = startIndex == 0 && key == "all";
            var startValue = fromStart ? 0 : window[0].Value;
            if (fromStart) flows += window[0].Flow;
            var gain = window[^1].Value - startValue - flows;

            var span = (window[^1].Date - window[0].Date).TotalDays;
            double? annualized = span >= 365 ? Math.Pow(growth, 365.0 / span) - 1 : null;
            double? bench = benchmark is { Count: > 0 } && benchmark.CloseNear(window[0].Date) is { } b0 and > 0 && benchmark.CloseNear(window[^1].Date) is { } b1
                ? (double)(b1 / b0) - 1 : null;
            result.Add(new PeriodPerformance(key, window[0].Date, window[^1].Date, growth - 1, annualized, Math.Round(gain, 2), bench));
        }
        return result;
    }
}
