using Poches.Core.Models;

namespace Poches.Core.Investments;

/// <param name="Benchmark">What the same cash would be worth had each purchase bought the benchmark instead.</param>
public sealed record EvolutionPoint(DateTime Date, decimal Invested, decimal Value, decimal? Benchmark);

/// <summary>Portfolio value, money invested and benchmark value from the first purchase to today. Pure function.</summary>
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
        DateTime today)
    {
        var known = holdings.ToDictionary(h => h.Id);
        var lots = purchases.Where(p => known.ContainsKey(p.HoldingId)).OrderBy(p => p.Date).ThenBy(p => p.Id).ToList();
        if (lots.Count == 0)
            return [];

        var first = lots[0].Date.Date;
        var last = today.Date > lots[^1].Date.Date ? today.Date : lots[^1].Date.Date;
        // Daily up to ~13 months, weekly beyond: enough points for a phone-wide chart.
        var step = (last - first).TotalDays <= 400 ? 1 : 7;
        var dates = new List<DateTime>();
        for (var d = first; d < last; d = d.AddDays(step))
            dates.Add(d);
        dates.Add(last);

        // Without market history (manual lines, or before a symbol's history starts) the price follows the
        // known points: each purchase price, then the manual price.
        var fallback = known.Values.ToDictionary(h => h.Id, h =>
        {
            var points = lots.Where(l => l.HoldingId == h.Id).Select(l => new DailyClose(l.Date.Date, l.UnitPrice)).ToList();
            if (h.ManualPrice is { } manual)
                points.Add(new DailyClose((h.ManualPriceDate ?? last).Date, manual));
            return (IReadOnlyList<DailyClose>)points.OrderBy(p => p.Date).ToList();
        });

        var bench = benchmark is { Count: > 0 } b ? b : null;
        var benchUnits = lots.Select(l => bench?.CloseNear(l.Date.Date) is { } p and > 0 ? l.Invested / p : 0m).ToList();

        var points = new List<EvolutionPoint>(dates.Count);
        foreach (var date in dates)
        {
            decimal invested = 0, value = 0, units = 0;
            for (var i = 0; i < lots.Count && lots[i].Date.Date <= date; i++)
            {
                invested += lots[i].Invested;
                value += lots[i].Quantity * PriceAt(lots[i], date);
                units += benchUnits[i];
            }

            decimal? benchValue = null;
            if (bench is not null && (date == last && benchmarkLatest is { } latest ? latest : bench.CloseNear(date)) is { } bp)
                benchValue = Math.Round(units * bp, 2);
            points.Add(new EvolutionPoint(date, Math.Round(invested, 2), Math.Round(value, 2), benchValue));
        }
        return points;

        decimal PriceAt(Purchase lot, DateTime date)
        {
            if (date == last && latestByHolding.TryGetValue(lot.HoldingId, out var latest))
                return latest;
            if (historyByHolding.TryGetValue(lot.HoldingId, out var history) && history.CloseOnOrBefore(date) is { } close)
                return close;
            return fallback[lot.HoldingId].CloseOnOrBefore(date) ?? lot.UnitPrice;
        }
    }
}
