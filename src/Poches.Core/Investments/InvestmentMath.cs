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

/// <param name="Invested">Cost of the units still held, fees and tax included, in the portfolio currency.</param>
/// <param name="Annualized">Yearly rate over the holding period; null below <see cref="InvestmentMath.MinDaysToAnnualize"/> days or once sold.</param>
/// <param name="Realized">Gain realized by the sales that took units of this purchase.</param>
public sealed record PurchasePerformance(
    Purchase Purchase,
    Holding Holding,
    decimal Invested,
    decimal Value,
    decimal Gain,
    double Return,
    int Days,
    double? Annualized,
    decimal RemainingQuantity,
    decimal SoldQuantity,
    decimal Realized)
{
    public bool SoldOut => RemainingQuantity <= 0;
}

/// <summary>
/// Units of one purchase taken by one sale. <see cref="TaxBasisPrice"/> is the cost per unit for the Belgian capital
/// gains tax: the purchase price, or the 31/12/2025 value when higher for purchases before 2026.
/// </summary>
public sealed record SaleSlice(Sale Sale, Purchase Purchase, decimal Quantity, decimal Cost, decimal Proceeds, decimal TaxBasisPrice)
{
    public decimal Gain => Proceeds - Cost;
}

/// <param name="AverageCost">Average cost per unit still held, fees included (PRU).</param>
/// <param name="Xirr">Money-weighted yearly return of the holding; null below 90 days of history.</param>
/// <param name="Realized">Gains realized by its sales.</param>
/// <param name="Dividends">Net dividends received.</param>
/// <param name="ReferencePrice">Value on 31/12/2025, for the Belgian capital gains tax.</param>
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
    double Weight,
    decimal Realized = 0,
    decimal Dividends = 0,
    IReadOnlyList<SaleSlice>? SaleSlices = null,
    IReadOnlyList<Sale>? SaleList = null,
    IReadOnlyList<Dividend>? DividendList = null,
    decimal? ReferencePrice = null)
{
    public bool Closed => Quantity <= 0;

    public IReadOnlyList<SaleSlice> Slices => SaleSlices ?? [];

    public IReadOnlyList<Sale> Sales => SaleList ?? [];

    public IReadOnlyList<Dividend> DividendsReceived => DividendList ?? [];

    public DateTime FirstPurchase => Purchases.Min(p => p.Purchase.Date);

    /// <summary>Unrealized and realized gains plus dividends.</summary>
    public decimal TotalGain => Gain + Realized + Dividends;
}

/// <param name="Invested">Cost of the units still held.</param>
/// <param name="Reinvested">Dividends paid back into purchases.</param>
/// <param name="ReinvestedHeld">Reinvested dividends in the cost of the units still held.</param>
public sealed record PortfolioPerformance(
    IReadOnlyList<HoldingPerformance> Holdings,
    decimal Invested,
    decimal Value,
    decimal Gain,
    double Return,
    double? Xirr,
    decimal? DayChange,
    double? DayChangeRatio,
    DateTime? FirstPurchase,
    decimal Realized = 0,
    decimal Dividends = 0,
    decimal Reinvested = 0,
    decimal ReinvestedHeld = 0)
{
    public bool IsEmpty => Holdings.Count == 0;

    /// <summary>Lines still held, biggest first.</summary>
    public IEnumerable<HoldingPerformance> Open => Holdings.Where(h => !h.Closed);

    public IEnumerable<PurchasePerformance> Purchases =>
        Holdings.SelectMany(h => h.Purchases).OrderBy(p => p.Purchase.Date).ThenBy(p => p.Purchase.Id);
}

/// <summary>Performance of each purchase, each holding and the whole portfolio. Pure functions.</summary>
public static class InvestmentMath
{
    /// <summary>Shorter periods are not annualised: a few days' move would read as an absurd yearly rate.</summary>
    public const int MinDaysToAnnualize = 90;

    /// <param name="prices">Prices in the portfolio currency by holding id (live or last known quotes).</param>
    /// <param name="referencePrices">Value on 31/12/2025 by holding id, from the price history (a holding's own reference wins).</param>
    public static PortfolioPerformance Compute(
        IReadOnlyList<Holding> holdings,
        IReadOnlyList<Purchase> purchases,
        IReadOnlyDictionary<int, MarketPrice> prices,
        DateTime today,
        IReadOnlyList<Sale>? sales = null,
        IReadOnlyList<Dividend>? dividends = null,
        IReadOnlyDictionary<int, decimal>? referencePrices = null)
    {
        var byHolding = purchases.GroupBy(p => p.HoldingId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Date).ThenBy(p => p.Id).ToList());
        var salesBy = (sales ?? []).GroupBy(s => s.HoldingId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Date).ThenBy(s => s.Id).ToList());
        var dividendsBy = (dividends ?? []).GroupBy(d => d.HoldingId).ToDictionary(g => g.Key, g => g.OrderBy(d => d.Date).ToList());
        var results = new List<HoldingPerformance>();
        var allFlows = new List<(DateTime, decimal)>();
        decimal dayBase = 0, dayChange = 0, reinvested = 0, reinvestedHeld = 0;
        var hasDayChange = false;

        foreach (var holding in holdings)
        {
            if (!byHolding.TryGetValue(holding.Id, out var lots) || lots.Count == 0)
                continue;
            var holdingSales = salesBy.GetValueOrDefault(holding.Id) ?? [];
            var holdingDividends = dividendsBy.GetValueOrDefault(holding.Id) ?? [];
            var reference = holding.ReferencePrice ?? referencePrices?.GetValueOrDefault(holding.Id);
            if (reference is 0) reference = null;

            var (price, priceDate, source, previousClose) = prices.TryGetValue(holding.Id, out var market)
                ? (market.Price, market.AsOf.Date, PriceSource.Quote, market.PreviousClose)
                : holding.ManualPrice is { } manual
                    ? (manual, (holding.ManualPriceDate ?? today).Date, PriceSource.Manual, (decimal?)null)
                    : (lots[^1].BaseUnitPrice, lots[^1].Date.Date, PriceSource.LastPurchase, (decimal?)null);

            var slices = MatchSales(lots, holdingSales, reference);
            var lotResults = new List<PurchasePerformance>();
            var flows = new List<(DateTime, decimal)>();
            decimal quantity = 0, invested = 0, value = 0;
            foreach (var lot in lots)
            {
                var sold = slices.Where(s => ReferenceEquals(s.Purchase, lot)).ToList();
                var soldQuantity = sold.Sum(s => s.Quantity);
                var remaining = Math.Max(0, lot.Quantity - soldQuantity);
                var lotInvested = lot.Quantity > 0 ? lot.Invested * remaining / lot.Quantity : 0;
                var lotValue = remaining * price;
                var days = Math.Max(0, (priceDate - lot.Date.Date).Days);
                lotResults.Add(new PurchasePerformance(lot, holding, lotInvested, lotValue, lotValue - lotInvested,
                    Ratio(lotValue - lotInvested, lotInvested), days, remaining > 0 ? Annualize(lotInvested, lotValue, days) : null,
                    remaining, soldQuantity, sold.Sum(s => s.Gain)));
                quantity += remaining;
                invested += lotInvested;
                value += lotValue;
                flows.Add((lot.Date.Date, -lot.Invested));
                reinvested += lot.FromDividends;
                if (lot.Quantity > 0) reinvestedHeld += lot.FromDividends * remaining / lot.Quantity;
            }
            flows.AddRange(holdingSales.Select(s => (s.Date.Date, s.Proceeds)));
            flows.AddRange(holdingDividends.Select(d => (d.Date.Date, d.Net)));
            if (value > 0)
                flows.Add((priceDate, value));
            allFlows.AddRange(flows);

            decimal? holdingDayChange = null;
            double? holdingDayRatio = null;
            if (previousClose is > 0 && source == PriceSource.Quote && quantity > 0)
            {
                holdingDayChange = quantity * (price - previousClose.Value);
                holdingDayRatio = (double)(price / previousClose.Value) - 1;
                dayBase += quantity * previousClose.Value;
                dayChange += holdingDayChange.Value;
                hasDayChange = true;
            }

            results.Add(new HoldingPerformance(holding, lotResults, quantity, invested, value, value - invested,
                Ratio(value - invested, invested), quantity > 0 ? invested / quantity : 0, price, priceDate, source,
                Xirr(flows), holdingDayChange, holdingDayRatio, 0, slices.Sum(s => s.Gain), holdingDividends.Sum(d => d.Net),
                slices, holdingSales, holdingDividends, reference));
        }

        var open = results.Where(r => !r.Closed).ToList();
        var totalValue = open.Sum(r => r.Value);
        var totalInvested = open.Sum(r => r.Invested);
        var weighted = results
            .Select(r => r with { Weight = totalValue > 0 ? (double)(r.Value / totalValue) : 0 })
            .OrderBy(r => r.Closed)
            .ThenByDescending(r => r.Value)
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
            results.Count > 0 ? results.Min(r => r.FirstPurchase) : null,
            results.Sum(r => r.Realized),
            results.Sum(r => r.Dividends),
            reinvested,
            reinvestedHeld);
    }

    /// <summary>Each sale takes units from the oldest purchases still held on its date.</summary>
    public static List<SaleSlice> MatchSales(IReadOnlyList<Purchase> lots, IReadOnlyList<Sale> sales, decimal? referencePrice)
    {
        var ordered = lots.OrderBy(l => l.Date).ThenBy(l => l.Id).ToList();
        var left = ordered.Select(l => l.Quantity).ToArray();
        var slices = new List<SaleSlice>();
        foreach (var sale in sales.OrderBy(s => s.Date).ThenBy(s => s.Id))
        {
            var want = sale.Quantity;
            for (var i = 0; i < ordered.Count && want > 0; i++)
            {
                if (ordered[i].Date.Date > sale.Date.Date || left[i] <= 0)
                    continue;
                var take = Math.Min(want, left[i]);
                left[i] -= take;
                want -= take;
                var lot = ordered[i];
                slices.Add(new SaleSlice(sale, lot, take, lot.Invested * take / lot.Quantity, sale.Proceeds * take / sale.Quantity,
                    TaxReport.BasisPrice(lot, referencePrice)));
            }
        }
        return slices;
    }

    /// <summary>Units of a holding held on a date, leaving out one sale (the one being edited).</summary>
    public static decimal HeldOn(IReadOnlyList<Purchase> purchases, IReadOnlyList<Sale> sales, int holdingId, DateTime date, int? skipSaleId = null) =>
        purchases.Where(p => p.HoldingId == holdingId && p.Date.Date <= date.Date).Sum(p => p.Quantity)
        - sales.Where(s => s.HoldingId == holdingId && s.Date.Date <= date.Date && s.Id != skipSaleId).Sum(s => s.Quantity);

    /// <summary>Whether some sale would take more units than were bought by its date.</summary>
    public static bool Oversold(IReadOnlyList<Purchase> purchases, IReadOnlyList<Sale> sales) =>
        sales.GroupBy(s => s.HoldingId).Any(g => g.Any(sale =>
            g.Where(s => s.Date.Date <= sale.Date.Date).Sum(s => s.Quantity)
            > purchases.Where(p => p.HoldingId == g.Key && p.Date.Date <= sale.Date.Date).Sum(p => p.Quantity) + 0.000001m));

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
