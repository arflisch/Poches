using Poches.Core.Data;
using Poches.Core.Investments;
using Poches.Core.Models;

namespace Poches.Services;

/// <summary>Everything the investment screens show, with the quotes that were available.</summary>
public sealed record InvestmentSnapshot(
    IReadOnlyList<Holding> Holdings,
    IReadOnlyList<Purchase> Purchases,
    IReadOnlyList<Sale> Sales,
    IReadOnlyList<Dividend> Dividends,
    InvestmentSettings Plan,
    PortfolioPerformance Performance,
    IReadOnlyDictionary<string, LiveQuote> Quotes,
    DateTime? QuotesAt,
    int MissingQuotes,
    TaxCountry TaxCountry);

public sealed record EvolutionResult(IReadOnlyList<EvolutionPoint> Points, IReadOnlyList<PeriodPerformance> Periods);

/// <summary>
/// A distribution Yahoo knows about and the portfolio doesn't: <see cref="Gross"/> in <see cref="Currency"/>,
/// <see cref="FxRate"/> set when that isn't the portfolio currency.
/// </summary>
public sealed record DetectedDividend(int HoldingId, string Line, string Key, DateTime Date, decimal PerShare, decimal Units, decimal Gross,
    string Currency, decimal? FxRate, decimal GrossBase);

/// <summary>
/// Loads the investments with their prices: live quotes when online quotes are on (cached a few minutes),
/// the last known quotes otherwise, then manual prices.
/// </summary>
public sealed class InvestmentService(BudgetStore store, MarketData market, AppSettings settings)
{
    public static readonly IReadOnlyList<(string Symbol, string Name)> Benchmarks =
    [
        ("CW8.PA", "MSCI World"),
        ("PSP5.PA", "S&P 500"),
        ("PUST.PA", "Nasdaq-100"),
        ("C40.PA", "CAC 40"),
    ];

    /// <summary>Belgian précompte mobilier, the estimate for a detected dividend.</summary>
    public const decimal BelgianWithholding = 0.30m;

    public async Task<InvestmentSnapshot> LoadAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var holdings = await store.GetHoldingsAsync();
        var purchases = await store.GetPurchasesAsync();
        var sales = await store.GetSalesAsync();
        var dividends = await store.GetDividendsAsync();
        var plan = await store.GetInvestmentSettingsAsync();
        var currency = settings.CurrencyCode;
        var online = settings.OnlineQuotes;
        var symbols = holdings.Where(h => h.Symbol is not null).Select(h => h.Symbol!).Distinct().ToList();
        var quotes = await market.GetQuotesAsync(symbols, currency, online, forceRefresh, cancellationToken);

        var prices = new Dictionary<int, MarketPrice>();
        foreach (var holding in holdings)
        {
            if (holding.Symbol is { } symbol && quotes.TryGetValue(symbol, out var quote) && quote is { Status: QuoteStatus.Ok, Price: { } price, AsOf: { } asOf })
                prices[holding.Id] = new MarketPrice(price, quote.PreviousClose, asOf.ToLocalTime());
        }

        // The value on 31/12/2025 is the cost of older units for the capital gains tax: the close of that day.
        var country = settings.TaxCountry;
        var belgian = country == TaxCountry.Belgium;
        var references = new Dictionary<int, decimal>();
        if (belgian)
        {
            foreach (var holding in holdings.Where(h => h.Symbol is not null && h.ReferencePrice is null && !BelgianTax.IsSheltered(h.Envelope)))
            {
                if (!purchases.Any(p => p.HoldingId == holding.Id && p.Date < BelgianTax.CapitalGainsStart))
                    continue;
                var history = await market.GetHistoryAsync(holding.Symbol!, BelgianTax.ReferenceDate.AddDays(-10), currency, online, cancellationToken);
                if (history?.Where(c => c.Date <= BelgianTax.ReferenceDate && c.Date >= BelgianTax.ReferenceDate.AddDays(-10)).LastOrDefault() is { } close)
                    references[holding.Id] = close.Close;
            }
        }

        var ok = quotes.Values.Where(q => q.Status == QuoteStatus.Ok && q.AsOf is not null).ToList();
        return new InvestmentSnapshot(
            holdings,
            purchases,
            sales,
            dividends,
            plan,
            InvestmentMath.Compute(holdings, purchases, prices, DateTime.Today, sales, dividends, references),
            quotes,
            ok.Count > 0 ? ok.Max(q => q.AsOf!.Value).ToLocalTime() : null,
            quotes.Values.Count(q => q.Status != QuoteStatus.Ok),
            country);
    }

    /// <summary>Value, money invested and benchmark from the first purchase to today, and the returns over usual periods.</summary>
    public async Task<EvolutionResult> LoadEvolutionAsync(InvestmentSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Performance.FirstPurchase is not { } first)
            return new EvolutionResult([], []);

        var currency = settings.CurrencyCode;
        var online = settings.OnlineQuotes;
        var histories = new Dictionary<int, IReadOnlyList<DailyClose>>();
        foreach (var holding in snapshot.Holdings.Where(h => h.Symbol is not null))
        {
            if (await market.GetHistoryAsync(holding.Symbol!, first, currency, online, cancellationToken) is { Count: > 0 } history)
                histories[holding.Id] = history;
        }

        var latest = snapshot.Performance.Holdings.ToDictionary(h => h.Holding.Id, h => h.Price);
        IReadOnlyList<DailyClose>? benchmark = null;
        decimal? benchmarkLatest = null;
        if (settings.Benchmark is { Length: > 0 } symbol)
        {
            benchmark = await market.GetHistoryAsync(symbol, first, currency, online, cancellationToken);
            var quote = await market.GetQuotesAsync([symbol], currency, online, false, cancellationToken);
            benchmarkLatest = quote.GetValueOrDefault(symbol)?.Price;
        }

        var points = Evolution.Compute(snapshot.Holdings, snapshot.Purchases, histories, latest, benchmark, benchmarkLatest, DateTime.Today,
            snapshot.Sales, snapshot.Dividends);
        return new EvolutionResult(points, points.Count >= 2 ? Evolution.Periods(points, benchmark, DateTime.Today) : []);
    }

    /// <summary>What the money is in, looking inside funds, and the running costs. Online only: null offline.</summary>
    public async Task<ExposureReport?> LoadExposureAsync(InvestmentSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!settings.OnlineQuotes || snapshot.Performance.IsEmpty)
            return null;
        var profiles = new Dictionary<int, InstrumentProfile>();
        var listing = new Dictionary<int, string>();
        foreach (var line in snapshot.Performance.Open)
        {
            if (line.Holding.Symbol is not { } symbol)
                continue;
            if (await market.GetProfileAsync(symbol, true, cancellationToken) is { } profile)
                profiles[line.Holding.Id] = profile;
            if (snapshot.Quotes.GetValueOrDefault(symbol)?.NativeCurrency is { Length: > 0 } native)
                listing[line.Holding.Id] = native;
        }
        return ExposureReport.Compute(snapshot.Performance, profiles, listing, settings.CurrencyCode);
    }

    /// <summary>A fund's running costs as Yahoo gives them, to suggest when editing the line. Online only.</summary>
    public async Task<decimal?> SuggestedTerAsync(string? symbol, CancellationToken cancellationToken) =>
        symbol is null || !settings.OnlineQuotes ? null : (await market.GetProfileAsync(symbol, true, cancellationToken))?.Ter;

    /// <summary>Distributions of the lines with a quote that aren't recorded yet, minus those ignored. Online only.</summary>
    public async Task<IReadOnlyList<DetectedDividend>> DetectDividendsAsync(InvestmentSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!settings.OnlineQuotes)
            return [];
        var portfolio = settings.CurrencyCode;
        var ignored = settings.IgnoredDividends;
        var today = DateTime.Today;
        var found = new List<DetectedDividend>();
        foreach (var line in snapshot.Performance.Holdings)
        {
            var holding = line.Holding;
            if (holding.Symbol is not { } symbol)
                continue;
            if (await market.GetDividendsAsync(symbol, line.FirstPurchase, true, cancellationToken) is not { Events.Count: > 0 } series)
                continue;
            var currency = series.Currency.Length > 0 ? series.Currency : portfolio;
            var foreign = currency != portfolio;
            if (foreign && !Operations.Currencies.Contains(currency))
                continue;
            var missing = DividendTools.Missing(
                snapshot.Purchases.Where(p => p.HoldingId == holding.Id).ToList(),
                snapshot.Sales.Where(s => s.HoldingId == holding.Id).ToList(),
                snapshot.Dividends.Where(d => d.HoldingId == holding.Id).ToList(),
                series.Events, today);
            if (missing.Count == 0)
                continue;
            var rates = foreign ? await market.GetFxRatesAsync(portfolio, currency, missing.Min(m => m.Date), today, true, cancellationToken) : null;
            foreach (var m in missing)
            {
                var key = $"{holding.Id}:{m.Date:yyyy-MM-dd}";
                var gross = Math.Round(m.Gross, 2);
                var rate = foreign ? FxTools.RateOn(rates, m.Date) : null;
                if (ignored.Contains(key) || gross < 0.01m || (foreign && rate is not > 0))
                    continue;
                found.Add(new DetectedDividend(holding.Id, holding.Name, key, m.Date, m.PerShare, m.Units, gross,
                    currency, rate, rate is { } r ? gross / r : gross));
            }
        }
        return found.OrderBy(d => d.Date).ToList();
    }

    /// <summary>The market rate of a day (units of <paramref name="currency"/> for one unit of the portfolio currency), from stored or fetched rates.</summary>
    public async Task<decimal?> MarketRateAsync(string currency, DateTime date, CancellationToken cancellationToken)
    {
        if (currency == settings.CurrencyCode)
            return 1m;
        var rates = await market.GetFxRatesAsync(settings.CurrencyCode, currency, date.AddDays(-10), date, settings.OnlineQuotes, cancellationToken);
        return FxTools.RateOn(rates, date);
    }

    /// <summary>Daily rates of each currency over the dates of a statement, for the import; null for a currency that couldn't be fetched.</summary>
    public async Task<Dictionary<string, IReadOnlyList<DailyClose>?>> MarketRatesAsync(IEnumerable<(string Currency, DateTime Date)> operations, CancellationToken cancellationToken)
    {
        var rates = new Dictionary<string, IReadOnlyList<DailyClose>?>();
        foreach (var group in operations.Where(o => o.Currency != settings.CurrencyCode && Operations.Currencies.Contains(o.Currency)).GroupBy(o => o.Currency))
        {
            try
            {
                rates[group.Key] = await market.GetFxRatesAsync(settings.CurrencyCode, group.Key, group.Min(o => o.Date).AddDays(-10), group.Max(o => o.Date), settings.OnlineQuotes, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rates[group.Key] = null;
            }
        }
        return rates;
    }

    public UsTaxReport UsTax(InvestmentSnapshot snapshot, int year) =>
        UsTaxReport.Compute(snapshot.Performance, snapshot.Purchases, snapshot.Dividends, snapshot.Plan, year, DateTime.Today);

    public TaxReport Tax(InvestmentSnapshot snapshot, int year) =>
        TaxReport.Compute(snapshot.Performance, snapshot.Purchases, snapshot.Sales, snapshot.Dividends, snapshot.Plan, year, DateTime.Today.Year);

    public Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, CancellationToken cancellationToken) =>
        market.SearchAsync(query, cancellationToken);

    /// <summary>The current quote of one symbol in the display currency, for the symbol picker.</summary>
    public async Task<LiveQuote?> PreviewAsync(string symbol, CancellationToken cancellationToken)
    {
        var quotes = await market.GetQuotesAsync([symbol], settings.CurrencyCode, settings.OnlineQuotes, false, cancellationToken);
        return quotes.GetValueOrDefault(symbol);
    }

    public static string BenchmarkName(string symbol) =>
        Benchmarks.FirstOrDefault(b => b.Symbol == symbol).Name ?? symbol;
}
