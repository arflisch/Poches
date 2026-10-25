using Poches.Core.Models;

namespace Poches.Core.Investments;

/// <summary>
/// Belgian rules applied when the Belgian tax option is on: the stock-exchange tax (TOB) on each purchase and sale,
/// and the 10 % capital gains tax from 2026 above a yearly exemption. The capital gains tax is new and its details
/// vary between sources: rate and exemption are settings, and the figures are estimates.
/// </summary>
public static class BelgianTax
{
    /// <summary>The three TOB rates and their cap per transaction (in euros).</summary>
    public static readonly IReadOnlyDictionary<decimal, decimal> TobCaps = new Dictionary<decimal, decimal>
    {
        [0.0012m] = 1300m,
        [0.0035m] = 1600m,
        [0.0132m] = 4000m,
    };

    /// <summary>Value on this date is the reference for gains on units bought before 2026.</summary>
    public static readonly DateTime ReferenceDate = new(2025, 12, 31);

    public static readonly DateTime CapitalGainsStart = new(2026, 1, 1);

    public static bool IsSheltered(TaxEnvelope envelope) => envelope != TaxEnvelope.Standard;

    /// <summary>Usual rate by type: 0.35 % for shares and real estate, 0.12 % for ETFs, funds and bonds, none for the rest.</summary>
    public static decimal DefaultTobRate(HoldingType type) => type switch
    {
        HoldingType.Stock or HoldingType.RealEstate => 0.0035m,
        HoldingType.Etf or HoldingType.Fund or HoldingType.Bond => 0.0012m,
        _ => 0m,
    };

    public static decimal TobRateFor(Holding holding) =>
        IsSheltered(holding.Envelope) ? 0m : holding.TobRate ?? DefaultTobRate(holding.Type);

    /// <summary>Tax on one transaction of <paramref name="amount"/>, capped, to the cent.</summary>
    public static decimal Tob(decimal rate, decimal amount) =>
        rate <= 0 || amount <= 0 ? 0 : Math.Round(Math.Min(rate * amount, TobCaps.TryGetValue(rate, out var cap) ? cap : decimal.MaxValue), 2, MidpointRounding.AwayFromZero);
}

public sealed record RealizedGain(string Line, TaxEnvelope Envelope, DateTime SaleDate, DateTime PurchaseDate, decimal Quantity,
    decimal SalePrice, decimal BasisPrice, decimal Gain, bool Sheltered);

public sealed record DividendTax(string Line, DateTime Date, decimal Gross, decimal Withholding);

/// <summary>
/// A year under the Belgian rules: realized gains and losses (10 % tax above the exemption, from 2026), what selling
/// every taxable line today would add, the TOB paid and the tax withheld on dividends. An estimate.
/// </summary>
public sealed record TaxReport(
    int Year,
    decimal Rate,
    decimal Exemption,
    bool Applies,
    IReadOnlyList<RealizedGain> Realized,
    decimal NetGain,
    decimal Taxable,
    decimal EstimatedTax,
    decimal LatentGain,
    decimal TaxIfSoldToday,
    decimal TobPaid,
    IReadOnlyList<DividendTax> Dividends,
    IReadOnlyList<string> ReyndersLines,
    IReadOnlyList<string> MissingReference)
{
    public decimal ExemptionLeft => Math.Max(0, Exemption - Math.Max(0, NetGain));

    public static TaxReport Compute(PortfolioPerformance performance, IReadOnlyList<Purchase> purchases, IReadOnlyList<Sale> sales,
        IReadOnlyList<Dividend> dividends, InvestmentSettings settings, int year, int currentYear)
    {
        var applies = year >= BelgianTax.CapitalGainsStart.Year;
        var realized = performance.Holdings
            .SelectMany(h => h.Slices.Where(s => s.Sale.Date.Year == year).Select(s => new RealizedGain(
                h.Holding.Name, h.Holding.Envelope, s.Sale.Date, s.Purchase.Date, s.Quantity, s.Sale.BaseUnitPrice, s.TaxBasisPrice,
                Math.Round(s.Quantity * (s.Sale.BaseUnitPrice - s.TaxBasisPrice), 2), BelgianTax.IsSheltered(h.Holding.Envelope))))
            .OrderBy(r => r.SaleDate)
            .ToList();
        var net = applies ? realized.Where(r => !r.Sheltered).Sum(r => r.Gain) : 0;
        var taxable = Math.Max(0, net - settings.Exemption);

        // Selling every taxable line today: only meaningful for the current year.
        decimal latent = 0;
        if (applies && year == currentYear)
        {
            latent = performance.Holdings
                .Where(h => !h.Closed && !BelgianTax.IsSheltered(h.Holding.Envelope))
                .SelectMany(h => h.Purchases.Where(p => p.RemainingQuantity > 0).Select(p =>
                    p.RemainingQuantity * (h.Price - BasisPrice(p.Purchase, h.ReferencePrice))))
                .Sum();
        }
        var ifSold = applies && year == currentYear ? settings.CapitalGainsRate * Math.Max(0, net + latent - settings.Exemption) : 0;

        var names = performance.Holdings.ToDictionary(h => h.Holding.Id, h => h.Holding.Name);
        var tob = purchases.Where(p => p.Date.Year == year).Sum(p => p.Tob / p.Rate) + sales.Where(s => s.Date.Year == year).Sum(s => s.Tob / s.Rate);
        var dividendLines = dividends
            .Where(d => d.Date.Year == year && names.ContainsKey(d.HoldingId))
            .OrderBy(d => d.Date)
            .Select(d => new DividendTax(names[d.HoldingId], d.Date, d.BaseGross, d.BaseWithholding))
            .ToList();
        // Lines held before 2026 with no value on 31/12/2025: their gain uses the purchase price.
        var missing = performance.Holdings
            .Where(h => !BelgianTax.IsSheltered(h.Holding.Envelope) && h.ReferencePrice is null && h.Purchases.Any(p => p.Purchase.Date < BelgianTax.CapitalGainsStart))
            .Select(h => h.Holding.Name)
            .ToList();

        return new TaxReport(year, settings.CapitalGainsRate, settings.Exemption, applies, realized, Math.Round(net, 2), Math.Round(taxable, 2),
            Math.Round(settings.CapitalGainsRate * taxable, 2), Math.Round(latent, 2), Math.Round(ifSold, 2), Math.Round(tob, 2), dividendLines,
            performance.Holdings.Where(h => !h.Closed && h.Holding.Reynders).Select(h => h.Holding.Name).ToList(), missing);
    }

    /// <summary>Base for the tax: the purchase price, or the 31/12/2025 value when higher for units bought before 2026.</summary>
    public static decimal BasisPrice(Purchase purchase, decimal? referencePrice) =>
        purchase.Date < BelgianTax.CapitalGainsStart && referencePrice is { } reference ? Math.Max(purchase.BaseUnitPrice, reference) : purchase.BaseUnitPrice;
}
