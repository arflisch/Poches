using Poches.Core.Models;

namespace Poches.Core.Investments;

/// <summary>
/// US rules applied when the US tax option is on: gains on units held more than a year are long-term (taxed at 0, 15
/// or 20 %), the others short-term (taxed as ordinary income); a net loss is deducted from other income up to a
/// yearly limit and the rest carried forward, keeping its character; a loss on a sale with the same security bought
/// within 30 days before or after (a wash sale) is not deductible. Retirement accounts, HSA and 529 plans are outside
/// all of it. Rates are settings and the figures estimates.
/// </summary>
public static class UsTax
{
    /// <summary>Days around a sale at a loss in which buying the same security makes it a wash sale.</summary>
    public const int WashSaleDays = 30;

    /// <summary>Lots that turn long-term within this many days are pointed out before selling them.</summary>
    public const int SoonLongTermDays = 90;

    /// <summary>Long-term once held more than a year: sold after the anniversary of the purchase.</summary>
    public static bool IsLongTerm(DateTime purchase, DateTime sale) => sale.Date > purchase.Date.AddYears(1);

    /// <summary>First day on which units bought on <paramref name="purchase"/> sell as long-term.</summary>
    public static DateTime LongTermFrom(DateTime purchase) => purchase.Date.AddYears(1).AddDays(1);
}

/// <param name="Disallowed">Part of the loss not deductible because of a wash sale.</param>
public sealed record UsRealizedLot(string Line, DateTime SaleDate, DateTime PurchaseDate, decimal Quantity, decimal Proceeds, decimal Cost,
    decimal Gain, bool LongTerm, decimal Disallowed);

/// <summary>A taxable lot with a gain that becomes long-term soon: waiting lowers the tax.</summary>
public sealed record UsSoonLongTerm(string Line, DateTime PurchaseDate, int Days, decimal Gain);

/// <summary>Short- and long-term amounts; negative for losses.</summary>
public readonly record struct UsGains(decimal ShortTerm, decimal LongTerm)
{
    public decimal Total => ShortTerm + LongTerm;
}

/// <summary>
/// A year under the US rules: realized gains by holding period, losses carried in from earlier years and out to the
/// next ones, the deduction from other income, an estimated federal (and state) tax on gains and dividends, and what
/// selling every taxable lot today would add. An estimate.
/// </summary>
/// <param name="Realized">Taxable lots sold in the year.</param>
/// <param name="YearGains">Gains of the year itself, wash-sale losses left out.</param>
/// <param name="CarryIn">Losses carried in from earlier years (negative).</param>
/// <param name="Net">After netting short- against long-term, before the deduction.</param>
/// <param name="CarryOut">Losses carried to the next year (negative).</param>
/// <param name="GainsTax">Tax on the net gains, minus what the loss deduction saves.</param>
/// <param name="ForeignTax">Tax withheld abroad on dividends: a credit against the US tax.</param>
public sealed record UsTaxReport(
    int Year,
    IReadOnlyList<UsRealizedLot> Realized,
    UsGains YearGains,
    UsGains CarryIn,
    UsGains Net,
    decimal LossDeduction,
    UsGains CarryOut,
    decimal GainsTax,
    decimal DividendsGross,
    decimal ForeignTax,
    decimal DividendTax,
    decimal WashSaleDisallowed,
    UsGains Latent,
    decimal TaxIfSoldToday,
    decimal HarvestableLosses,
    IReadOnlyList<UsSoonLongTerm> SoonLongTerm)
{
    public decimal EstimatedTax => GainsTax + DividendTax;

    public static UsTaxReport Compute(PortfolioPerformance performance, IReadOnlyList<Purchase> purchases, IReadOnlyList<Dividend> dividends,
        InvestmentSettings settings, int year, DateTime today)
    {
        var taxable = performance.Holdings.Where(h => !BelgianTax.IsSheltered(h.Holding.Envelope)).ToList();
        var lots = taxable
            .SelectMany(h => h.Slices.Select(s => Lot(h.Holding, s, h.Slices, purchases)))
            .ToList();

        // Losses carry forward year after year from the first sale: each year is netted with what came in.
        var carry = new UsGains(0, 0);
        var years = lots.Select(l => l.Lot.SaleDate.Year).Where(y => y < year).Distinct().Order().ToList();
        if (years.Count > 0)
        {
            for (var y = years[0]; y < year; y++)
                (_, carry, _) = Offset(Sum(lots.Where(l => l.Lot.SaleDate.Year == y)), carry, settings.UsLossLimit);
        }
        var carryIn = carry;
        var yearGains = Sum(lots.Where(l => l.Lot.SaleDate.Year == year));
        var (net, carryOut, deduction) = Offset(yearGains, carryIn, settings.UsLossLimit);
        var gainsTax = TaxOn(net, deduction, settings);

        var taxableIds = taxable.Select(h => h.Holding.Id).ToHashSet();
        var dividendsOfYear = dividends.Where(d => d.Date.Year == year && taxableIds.Contains(d.HoldingId)).ToList();
        var gross = dividendsOfYear.Sum(d => d.BaseGross);
        var foreign = dividendsOfYear.Sum(d => d.BaseWithholding);
        // Most dividends of US shares and ETFs are qualified: taxed like long-term gains, less the foreign tax credit.
        var dividendTax = Math.Max(0, gross * (settings.UsLongTermRate + settings.UsNiitRate + settings.UsStateRate) - foreign);

        // What selling every taxable lot today would add, this year only.
        UsGains latent = default;
        decimal ifSold = 0, harvest = 0;
        var soon = new List<UsSoonLongTerm>();
        if (year == today.Year)
        {
            foreach (var h in taxable.Where(h => !h.Closed))
            {
                foreach (var p in h.Purchases.Where(p => p.RemainingQuantity > 0))
                {
                    var longTerm = UsTax.IsLongTerm(p.Purchase.Date, today);
                    latent = longTerm ? latent with { LongTerm = latent.LongTerm + p.Gain } : latent with { ShortTerm = latent.ShortTerm + p.Gain };
                    if (p.Gain < 0)
                        harvest -= p.Gain;
                    var days = (UsTax.LongTermFrom(p.Purchase.Date) - today.Date).Days;
                    if (!longTerm && p.Gain > 0 && days <= UsTax.SoonLongTermDays)
                        soon.Add(new UsSoonLongTerm(h.Holding.Name, p.Purchase.Date, days, Math.Round(p.Gain, 2)));
                }
            }
            var (allNet, _, allDeduction) = Offset(new UsGains(yearGains.ShortTerm + latent.ShortTerm, yearGains.LongTerm + latent.LongTerm), carryIn, settings.UsLossLimit);
            ifSold = TaxOn(allNet, allDeduction, settings) - gainsTax;
        }

        return new UsTaxReport(year, lots.Where(l => l.Lot.SaleDate.Year == year).Select(l => l.Lot).OrderBy(l => l.SaleDate).ToList(),
            Round(yearGains), Round(carryIn), Round(net), Math.Round(deduction, 2), Round(carryOut), Math.Round(gainsTax, 2),
            Math.Round(gross, 2), Math.Round(foreign, 2), Math.Round(dividendTax, 2),
            Math.Round(lots.Where(l => l.Lot.SaleDate.Year == year).Sum(l => l.Lot.Disallowed), 2),
            Round(latent), Math.Round(ifSold, 2), Math.Round(harvest, 2), soon.OrderBy(s => s.Days).ToList());
    }

    /// <summary>
    /// Nets a year's gains with the losses carried in: short against short and long against long, then one against the
    /// other; a remaining loss is deducted from other income up to <paramref name="limit"/> (short-term first) and the
    /// rest carried forward with its character.
    /// </summary>
    public static (UsGains Net, UsGains CarryOut, decimal Deduction) Offset(UsGains year, UsGains carryIn, decimal limit)
    {
        var st = year.ShortTerm + carryIn.ShortTerm;
        var lt = year.LongTerm + carryIn.LongTerm;
        if (st < 0 && lt > 0)
        {
            var total = st + lt;
            (st, lt) = total >= 0 ? (0m, total) : (total, 0m);
        }
        else if (lt < 0 && st > 0)
        {
            var total = st + lt;
            (st, lt) = total >= 0 ? (total, 0m) : (0m, total);
        }
        var net = new UsGains(st, lt);
        var loss = -(Math.Min(st, 0) + Math.Min(lt, 0));
        var deduction = Math.Min(Math.Max(0, limit), loss);
        var fromShort = Math.Min(deduction, -Math.Min(st, 0));
        var carryOut = new UsGains(Math.Min(st, 0) + fromShort, Math.Min(lt, 0) + (deduction - fromShort));
        return (net, carryOut, deduction);
    }

    /// <summary>Tax on the net gains, minus what deducting a loss from other income saves.</summary>
    private static decimal TaxOn(UsGains net, decimal deduction, InvestmentSettings s)
    {
        var extra = s.UsNiitRate + s.UsStateRate;
        return Math.Max(0, net.ShortTerm) * (s.UsShortTermRate + extra)
            + Math.Max(0, net.LongTerm) * (s.UsLongTermRate + extra)
            - deduction * (s.UsShortTermRate + s.UsStateRate);
    }

    private static (UsRealizedLot Lot, decimal Counted) Lot(Holding holding, SaleSlice slice, IReadOnlyList<SaleSlice> slices, IReadOnlyList<Purchase> purchases)
    {
        var gain = slice.Gain;
        decimal disallowed = 0;
        if (gain < 0)
        {
            // Units of the same security bought within 30 days of the sale, other than those it sold.
            var sale = slice.Sale.Date.Date;
            var sold = slices.Where(s => s.Sale.Id == slice.Sale.Id).Select(s => s.Purchase.Id).ToHashSet();
            var replacement = purchases
                .Where(p => p.HoldingId == holding.Id && !sold.Contains(p.Id) && Math.Abs((p.Date.Date - sale).TotalDays) <= UsTax.WashSaleDays)
                .Sum(p => p.Quantity);
            if (replacement > 0 && slice.Quantity > 0)
                disallowed = -gain * Math.Min(1, replacement / slice.Quantity);
        }
        var lot = new UsRealizedLot(holding.Name, slice.Sale.Date, slice.Purchase.Date, slice.Quantity, Math.Round(slice.Proceeds, 2), Math.Round(slice.Cost, 2),
            Math.Round(gain, 2), UsTax.IsLongTerm(slice.Purchase.Date, slice.Sale.Date), Math.Round(disallowed, 2));
        return (lot, gain + disallowed);
    }

    private static UsGains Sum(IEnumerable<(UsRealizedLot Lot, decimal Counted)> lots) =>
        lots.Aggregate(new UsGains(0, 0), (sum, l) => l.Lot.LongTerm
            ? sum with { LongTerm = sum.LongTerm + l.Counted }
            : sum with { ShortTerm = sum.ShortTerm + l.Counted });

    private static UsGains Round(UsGains g) => new(Math.Round(g.ShortTerm, 2), Math.Round(g.LongTerm, 2));
}
