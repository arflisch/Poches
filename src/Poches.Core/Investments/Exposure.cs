using Poches.Core.Models;

namespace Poches.Core.Investments;

public sealed record ExposureSlice(string Key, decimal Value, double Weight);

/// <param name="Source">entered, yahoo or unknown.</param>
public sealed record TerLine(string Line, decimal? Ter, string Source, decimal Value);

/// <summary>
/// What the money is really in, looking inside funds where Yahoo describes them: sectors, asset classes, listing
/// currencies and, for shares, countries; plus the yearly running costs (TER) weighted by value. Keys are neutral
/// (stocks, unknown…) and translated by the app; sectors and countries are Yahoo's.
/// </summary>
public sealed record ExposureReport(
    decimal Value,
    IReadOnlyList<ExposureSlice> BySector,
    IReadOnlyList<ExposureSlice> ByAssetClass,
    IReadOnlyList<ExposureSlice> ByCurrency,
    IReadOnlyList<ExposureSlice> ByCountry,
    double Coverage,
    decimal? WeightedTer,
    decimal AnnualCost,
    IReadOnlyList<TerLine> Ter,
    IReadOnlyList<string> ReyndersHints)
{
    public const string Unknown = "unknown";
    public const string Funds = "funds";
    public const string NonEquity = "non_equity";
    public const string Unlisted = "unlisted";

    public static ExposureReport Compute(
        PortfolioPerformance performance,
        IReadOnlyDictionary<int, InstrumentProfile> profiles,
        IReadOnlyDictionary<int, string> listingCurrencies,
        string portfolioCurrency)
    {
        var sectors = new Dictionary<string, decimal>();
        var classes = new Dictionary<string, decimal>();
        var currencies = new Dictionary<string, decimal>();
        var countries = new Dictionary<string, decimal>();
        decimal covered = 0;
        var ter = new List<TerLine>();
        var reynders = new List<string>();

        foreach (var h in performance.Open)
        {
            var value = h.Value;
            var holding = h.Holding;
            profiles.TryGetValue(holding.Id, out var profile);
            Add(currencies, listingCurrencies.GetValueOrDefault(holding.Id) ?? (holding.Symbol is null ? Unlisted : portfolioCurrency), value);

            // Asset classes: the fund's own split when known, otherwise the line's type.
            if (profile is { Stocks: not null } or { Bonds: not null })
            {
                var stocks = profile.Stocks ?? 0;
                var bonds = profile.Bonds ?? 0;
                var cash = profile.Cash ?? 0;
                Add(classes, "stocks", value * stocks);
                Add(classes, "bonds", value * bonds);
                Add(classes, "cash", value * cash);
                Add(classes, "other", value * Math.Max(0, 1 - stocks - bonds - cash));
                if (bonds > 0.10m && !holding.Reynders)
                    reynders.Add(holding.Name);
            }
            else
            {
                Add(classes, holding.Type switch
                {
                    HoldingType.Stock => "stocks",
                    HoldingType.Bond => "bonds",
                    HoldingType.Cash => "cash",
                    HoldingType.RealEstate => "realestate",
                    HoldingType.Crypto => "crypto",
                    HoldingType.Etf or HoldingType.Fund => Unknown,
                    _ => "other",
                }, value);
            }

            // Sectors: shares carry their own; funds spread their share part over their sector weights.
            if (profile is { Sectors.Count: > 0 })
            {
                var equity = profile.Kind == "EQUITY" ? 1m : profile.Stocks ?? 1m;
                var total = profile.Sectors.Values.Sum();
                foreach (var (sector, weight) in profile.Sectors)
                    Add(sectors, sector, value * equity * (total > 0 ? weight / total : 0));
                if (equity < 1) Add(sectors, NonEquity, value * (1 - equity));
                covered += value;
            }
            else
            {
                Add(sectors, Unknown, value);
            }

            Add(countries, profile switch
            {
                { Kind: "EQUITY", Country: { Length: > 0 } country } => country,
                { Kind: "ETF" or "MUTUALFUND" } => Funds,
                _ when holding.Type is HoldingType.Etf or HoldingType.Fund => Funds,
                _ => Unknown,
            }, value);

            var lineTer = holding.Ter ?? profile?.Ter;
            if (holding.Type is HoldingType.Etf or HoldingType.Fund || lineTer is not null)
                ter.Add(new TerLine(holding.Name, lineTer, holding.Ter is not null ? "entered" : profile?.Ter is not null ? "yahoo" : "unknown", value));
        }

        var portfolioValue = performance.Value;
        var known = ter.Where(t => t.Ter is not null).ToList();
        var knownValue = known.Sum(t => t.Value);
        var annualCost = known.Sum(t => t.Value * t.Ter!.Value);
        return new ExposureReport(portfolioValue, Slices(sectors, portfolioValue), Slices(classes, portfolioValue), Slices(currencies, portfolioValue),
            Slices(countries, portfolioValue), portfolioValue > 0 ? (double)(covered / portfolioValue) : 0, knownValue > 0 ? Math.Round(annualCost / knownValue, 6) : null, Math.Round(annualCost, 2), ter, reynders);
    }

    private static void Add(Dictionary<string, decimal> map, string key, decimal value)
    {
        if (value > 0) map[key] = map.GetValueOrDefault(key) + value;
    }

    private static List<ExposureSlice> Slices(Dictionary<string, decimal> map, decimal total) =>
        map.OrderByDescending(kv => kv.Value)
            .Select(kv => new ExposureSlice(kv.Key, Math.Round(kv.Value, 2), total > 0 ? (double)(kv.Value / total) : 0))
            .ToList();
}
