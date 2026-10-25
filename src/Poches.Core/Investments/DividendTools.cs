using Poches.Core.Models;

namespace Poches.Core.Investments;

/// <summary>A distribution per unit: its ex-dividend date (units held the day before receive it) and amount, in the listing's currency.</summary>
public sealed record DividendEvent(DateTime Date, decimal Amount);

/// <summary>A distribution the portfolio doesn't have yet: units held the day before the ex-date times the amount per unit.</summary>
public sealed record MissingDividend(DateTime Date, decimal PerShare, decimal Units, decimal Gross);

public static class DividendTools
{
    /// <summary>
    /// Dividends still free on a date to pay a purchase: net dividends received minus those already reinvested, taken at
    /// its lowest from that date on, so a later purchase never ends up paid with dividends spent in between.
    /// <paramref name="purchases"/>: the purchases to count (leave out the one being edited).
    /// </summary>
    public static decimal Room(IEnumerable<Dividend> dividends, IEnumerable<Purchase> purchases, DateTime date)
    {
        var events = dividends.Select(d => (Date: d.Date.Date, Amount: d.Net, Order: 0))
            .Concat(purchases.Where(p => p.FromDividends > 0).Select(p => (Date: p.Date.Date, Amount: -p.FromDividends, Order: 1)))
            .OrderBy(e => e.Date).ThenBy(e => e.Order)
            .ToList();
        decimal balance = 0;
        var i = 0;
        for (; i < events.Count && events[i].Date <= date.Date; i++) balance += events[i].Amount;
        var room = balance;
        for (; i < events.Count; i++)
        {
            balance += events[i].Amount;
            room = Math.Min(room, balance);
        }
        return Math.Max(0, room);
    }

    /// <summary>
    /// Distributions of one holding that are not recorded yet. Units count when bought before the ex-date and not sold
    /// before it. A dividend recorded from a week before to 75 days after the ex-date (payment comes a few weeks later)
    /// accounts for it; each one accounts for one distribution.
    /// </summary>
    public static IReadOnlyList<MissingDividend> Missing(
        IReadOnlyList<Purchase> purchases, IReadOnlyList<Sale> sales, IReadOnlyList<Dividend> dividends, IEnumerable<DividendEvent> events, DateTime today)
    {
        var used = new HashSet<int>();
        var missing = new List<MissingDividend>();
        foreach (var ev in events.OrderBy(e => e.Date))
        {
            var exDate = ev.Date.Date;
            if (exDate > today.Date || ev.Amount <= 0) continue;
            var units = purchases.Where(p => p.Date.Date < exDate).Sum(p => p.Quantity) - sales.Where(s => s.Date.Date < exDate).Sum(s => s.Quantity);
            if (units <= 0.000001m) continue;
            var (lo, hi) = (exDate.AddDays(-7), exDate.AddDays(75));
            var best = -1;
            for (var i = 0; i < dividends.Count; i++)
            {
                var d = dividends[i].Date.Date;
                if (used.Contains(i) || d < lo || d > hi) continue;
                if (best < 0 || Math.Abs((d - exDate).TotalDays) < Math.Abs((dividends[best].Date.Date - exDate).TotalDays)) best = i;
            }
            if (best >= 0) { used.Add(best); continue; }
            missing.Add(new MissingDividend(exDate, ev.Amount, units, units * ev.Amount));
        }
        return missing;
    }
}

public static class FxTools
{
    /// <summary>The rate of a date in daily closes sorted by date: the last one on or before it, else the first one.</summary>
    public static decimal? RateOn(IReadOnlyList<DailyClose>? rates, DateTime date) =>
        rates is { Count: > 0 } ? rates.CloseNear(date.Date) : null;

    /// <summary>
    /// Banks quote EUR/USD (dollars for one euro) or USD/EUR (euros for one dollar). A rate is turned around when its
    /// inverse is closer to the reference, the market rate of that day in the first convention.
    /// </summary>
    public static decimal? OrientRate(decimal rate, decimal? reference)
    {
        if (rate <= 0) return null;
        if (reference is not > 0) return rate;
        double Off(decimal r) => Math.Abs(Math.Log((double)(r / reference.Value)));
        return Off(1 / rate) < Off(rate) ? 1 / rate : rate;
    }
}
