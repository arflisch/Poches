using Poches.Core.Data;
using Poches.Core.Investments;
using Poches.Core.Models;

namespace Poches.Services;

/// <summary>Everything the investment screens show, with the quotes that were available.</summary>
public sealed record InvestmentSnapshot(
    IReadOnlyList<Holding> Holdings,
    IReadOnlyList<Purchase> Purchases,
    PortfolioPerformance Performance,
    IReadOnlyDictionary<string, LiveQuote> Quotes,
    DateTime? QuotesAt,
    int MissingQuotes);

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

    public async Task<InvestmentSnapshot> LoadAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var holdings = await store.GetHoldingsAsync();
        var purchases = await store.GetPurchasesAsync();
        var symbols = holdings.Where(h => h.Symbol is not null).Select(h => h.Symbol!).Distinct().ToList();
        var quotes = await market.GetQuotesAsync(symbols, settings.CurrencyCode, settings.OnlineQuotes, forceRefresh, cancellationToken);

        var prices = new Dictionary<int, MarketPrice>();
        foreach (var holding in holdings)
        {
            if (holding.Symbol is { } symbol && quotes.TryGetValue(symbol, out var quote) && quote is { Status: QuoteStatus.Ok, Price: { } price, AsOf: { } asOf })
                prices[holding.Id] = new MarketPrice(price, quote.PreviousClose, asOf.ToLocalTime());
        }

        var ok = quotes.Values.Where(q => q.Status == QuoteStatus.Ok && q.AsOf is not null).ToList();
        return new InvestmentSnapshot(
            holdings,
            purchases,
            InvestmentMath.Compute(holdings, purchases, prices, DateTime.Today),
            quotes,
            ok.Count > 0 ? ok.Max(q => q.AsOf!.Value).ToLocalTime() : null,
            quotes.Values.Count(q => q.Status != QuoteStatus.Ok));
    }

    /// <summary>Value, money invested and benchmark from the first purchase to today.</summary>
    public async Task<IReadOnlyList<EvolutionPoint>> LoadEvolutionAsync(InvestmentSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Performance.FirstPurchase is not { } first)
            return [];

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

        return Evolution.Compute(snapshot.Holdings, snapshot.Purchases, histories, latest, benchmark, benchmarkLatest, DateTime.Today);
    }

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
