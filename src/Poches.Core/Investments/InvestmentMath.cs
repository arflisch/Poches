using Poches.Core.Models;

namespace Poches.Core.Investments;

/// <summary>Where a holding's current price comes from.</summary>
public enum PriceSource
{
    /// <summary>Online quote (live, or the last one fetched).</summary>
    Quote,
    Manual,
    LastPurchase,
}

/// <summary>A price in the portfolio currency, with the previous close for the day's change.</summary>
public sealed record MarketPrice(decimal Price, decimal? PreviousClose, DateTime AsOf);

/// <param name="Annualized">Yearly rate over the holding period; null below <see cref="InvestmentMath.MinDaysToAnnualize"/> days.</param>
public sealed record PurchasePerformance(
    Purchase Purchase,
    Holding Holding,
    decimal Invested,
    decimal Value,
    decimal Gain,
    double Return,
    int Days,
    double? Annualized);

/// <param name="AverageCost">Average cost per unit, fees included (PRU).</param>
/// <param name="Xirr">Money-weighted yearly return of the holding; null below 90 days of history.</param>
public sealed record HoldingPerformance(
    Holding Holding,
    IReadOnlyList<PurchasePerformance> Purchases,
    decimal Quantity,
    decimal Invested,
    decimal Value,
    decimal Gain,
    double Return,
    decimal AverageCost,
    decimal Price,
    DateTime PriceDate,
    PriceSource Source,
    double? Xirr,
    decimal? DayChange,
    double? DayChangeRatio,
    double Weight);

public sealed record PortfolioPerformance(
    IReadOnlyList<HoldingPerformance> Holdings,
    decimal Invested,
    decimal Value,
    decimal Gain,
    double Return,
    double? Xirr,
    decimal? DayChange,
    double? DayChangeRatio,
    DateTime? FirstPurchase)
{
    public bool IsEmpty => Holdings.Count == 0;

    public IEnumerable<PurchasePerformance> Purchases =>
        Holdings.SelectMany(h => h.Purchases).OrderBy(p => p.Purchase.Date).ThenBy(p => p.Purchase.Id);
}

/// <summary>Performance of each purchase, each holding and the whole portfolio. Pure functions.</summary>
public static class InvestmentMath
{
    /// <summary>Shorter periods are not annualised: a few days' move would read as an absurd yearly rate.</summary>
    public const int MinDaysToAnnualize = 90;

    /// <param name="prices">Prices in the portfolio currency by holding id (live or last known quotes).</param>
    public static PortfolioPerformance Compute(
        IReadOnlyList<Holding> holdings,
        IReadOnlyList<Purchase> purchases,
        IReadOnlyDictionary<int, MarketPrice> prices,
        DateTime today)
    {
        var byHolding = purchases.GroupBy(p => p.HoldingId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Date).ThenBy(p => p.Id).ToList());
        var results = new List<HoldingPerformance>();
        var allFlows = new List<(DateTime, decimal)>();
        decimal dayBase = 0, dayChange = 0;
        var hasDayChange = false;

        foreach (var holding in holdings)
        {
            if (!byHolding.TryGetValue(holding.Id, out var lots) || lots.Count == 0)
                continue;

            var (price, priceDate, source, previousClose) = prices.TryGetValue(holding.Id, out var market)
                ? (market.Price, market.AsOf.Date, PriceSource.Quote, market.PreviousClose)
                : holding.ManualPrice is { } manual
                    ? (manual, (holding.ManualPriceDate ?? today).Date, PriceSource.Manual, (decimal?)null)
                    : (lots[^1].UnitPrice, lots[^1].Date.Date, PriceSource.LastPurchase, (decimal?)null);

            var lotResults = new List<PurchasePerformance>();
            var flows = new List<(DateTime, decimal)>();
            decimal quantity = 0, invested = 0, value = 0;
            foreach (var lot in lots)
            {
                var lotInvested = lot.Invested;
                var lotValue = lot.Quantity * price;
                var days = Math.Max(0, (priceDate - lot.Date.Date).Days);
                lotResults.Add(new PurchasePerformance(lot, holding, lotInvested, lotValue, lotValue - lotInvested,
                    Ratio(lotValue - lotInvested, lotInvested), days, Annualize(lotInvested, lotValue, days)));
                quantity += lot.Quantity;
                invested += lotInvested;
                value += lotValue;
                flows.Add((lot.Date.Date, -lotInvested));
            }
            flows.Add((priceDate, value));
            allFlows.AddRange(flows);

            decimal? holdingDayChange = null;
            double? holdingDayRatio = null;
            if (previousClose is > 0 && source == PriceSource.Quote)
            {
                holdingDayChange = quantity * (price - previousClose.Value);
                holdingDayRatio = (double)(price / previousClose.Value) - 1;
                dayBase += quantity * previousClose.Value;
                dayChange += holdingDayChange.Value;
                hasDayChange = true;
            }

            results.Add(new HoldingPerformance(holding, lotResults, quantity, invested, value, value - invested,
                Ratio(value - invested, invested), quantity > 0 ? invested / quantity : 0, price, priceDate, source,
                Xirr(flows), holdingDayChange, holdingDayRatio, 0));
        }

        var totalValue = results.Sum(r => r.Value);
        var totalInvested = results.Sum(r => r.Invested);
        var weighted = results
            .Select(r => r with { Weight = totalValue > 0 ? (double)(r.Value / totalValue) : 0 })
            .OrderByDescending(r => r.Value)
            .ThenBy(r => r.Holding.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new PortfolioPerformance(
            weighted,
            totalInvested,
            totalValue,
            totalValue - totalInvested,
            Ratio(totalValue - totalInvested, totalInvested),
            Xirr(allFlows),
            hasDayChange ? dayChange : null,
            hasDayChange && dayBase > 0 ? (double)(dayChange / dayBase) : null,
            purchases.Count > 0 ? purchases.Min(p => p.Date) : null);
    }

    public static double? Annualize(decimal invested, decimal value, int days) =>
        days >= MinDaysToAnnualize && invested > 0 && value > 0
            ? Math.Pow((double)(value / invested), 365.0 / days) - 1
            : null;

    /// <summary>
    /// Money-weighted yearly return (XIRR): the rate at which the dated cash flows (purchases negative, current
    /// value positive) sum to zero. Null below 90 days of history or when it cannot be solved.
    /// </summary>
    public static double? Xirr(IReadOnlyList<(DateTime Date, decimal Amount)> flows)
    {
        if (flows.Count < 2 || !flows.Any(f => f.Amount < 0) || !flows.Any(f => f.Amount > 0))
            return null;
        var start = flows.Min(f => f.Date);
        if ((flows.Max(f => f.Date) - start).TotalDays < MinDaysToAnnualize)
            return null;

        var points = flows.Select(f => (Amount: (double)f.Amount, Years: (f.Date - start).TotalDays / 365.0)).ToList();
        double Npv(double rate) => points.Sum(p => p.Amount / Math.Pow(1 + rate, p.Years));

        double lo = -0.9999, hi = 10, flo = Npv(lo), fhi = Npv(hi);
        if (!double.IsFinite(flo) || !double.IsFinite(fhi) || flo * fhi > 0)
            return null;
        for (var i = 0; i < 200; i++)
        {
            var mid = (lo + hi) / 2;
            var fm = Npv(mid);
            if (Math.Abs(fm) < 1e-9 || hi - lo < 1e-10)
                return mid;
            if (flo * fm < 0) hi = mid;
            else { lo = mid; flo = fm; }
        }
        return (lo + hi) / 2;
    }

    private static double Ratio(decimal part, decimal whole) => whole > 0 ? (double)(part / whole) : 0;
}
