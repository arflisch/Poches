using Poches.Core.Data;
using Poches.Core.Models;

namespace Poches.Core.Investments;

public enum QuoteStatus
{
    Ok,
    NotFound,
    Unavailable,
}

/// <summary>A quote converted to the portfolio currency.</summary>
public sealed record LiveQuote(string Symbol, QuoteStatus Status, decimal? Price, decimal? PreviousClose, DateTime? AsOf, string? Name, string? NativeCurrency)
{
    public static LiveQuote Missing(string symbol, QuoteStatus status) => new(symbol, status, null, null, null, null, null);
}

/// <summary>
/// Quotes and daily histories for the holdings, converted to the portfolio currency. Everything fetched is
/// kept on the device, so the last known values stay available offline (or once online quotes are switched off).
/// </summary>
public sealed class MarketData(BudgetStore store, IQuoteSource source, TimeProvider? timeProvider = null)
{
    /// <summary>A quote younger than this is reused instead of asking Yahoo again.</summary>
    public static readonly TimeSpan QuoteLifetime = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan HistoryLifetime = TimeSpan.FromHours(6);
    private const int MaxParallelCalls = 4;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _historyGate = new(1, 1);

    /// <summary>
    /// Quotes for <paramref name="symbols"/> in <paramref name="currency"/> (ISO code). With <paramref name="online"/>
    /// false, or when Yahoo cannot be reached, the last stored quotes are used.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, LiveQuote>> GetQuotesAsync(
        IEnumerable<string> symbols, string currency, bool online, bool force, CancellationToken cancellationToken)
    {
        var wanted = symbols.Where(Symbols.IsValid).Select(s => s.ToUpperInvariant()).Distinct().ToList();
        var results = new Dictionary<string, LiveQuote>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
            return results;

        using var gate = new SemaphoreSlim(MaxParallelCalls);
        var tasks = wanted.Select(async symbol =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return (symbol, quote: await GetQuoteAsync(symbol, currency, online, force, cancellationToken));
            }
            finally
            {
                gate.Release();
            }
        });
        foreach (var (symbol, quote) in await Task.WhenAll(tasks))
            results[symbol] = quote;
        return results;
    }

    public async Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var text = query.Trim();
        return text.Length is < 2 or > 40 ? [] : await source.SearchAsync(text, cancellationToken);
    }

    /// <summary>
    /// Daily closes from a little before <paramref name="from"/> to today in <paramref name="currency"/>, or null
    /// when none are known. New closes are fetched only when <paramref name="online"/> is true.
    /// </summary>
    public async Task<IReadOnlyList<DailyClose>?> GetHistoryAsync(string symbol, DateTime from, string currency, bool online, CancellationToken cancellationToken)
    {
        if (!Symbols.IsValid(symbol))
            return null;
        var series = await GetStoredHistoryAsync(symbol.ToUpperInvariant(), from, online, cancellationToken);
        if (series is null || series.Closes.Count == 0)
            return null;

        var (factor, native) = YahooParser.MinorUnit(series.Currency);
        if (native.Length == 0 || native == currency)
            return factor == 1m ? series.Closes : series.Closes.Select(c => c with { Close = c.Close * factor }).ToList();

        var fx = await GetStoredHistoryAsync(FxSymbol(native, currency), from, online, cancellationToken);
        if (fx is null || fx.Closes.Count == 0)
            return null;
        return series.Closes.Select(c => c with { Close = Math.Round(c.Close * factor * fx.Closes.CloseNear(c.Date)!.Value, 6) }).ToList();
    }

    private async Task<LiveQuote> GetQuoteAsync(string symbol, string currency, bool online, bool force, CancellationToken cancellationToken)
    {
        var quote = await GetRawQuoteAsync(symbol, online, force, cancellationToken);
        if (quote.Status != QuoteStatus.Ok || quote.Cached is null)
            return LiveQuote.Missing(symbol, quote.Status);

        var cached = quote.Cached;
        var (factor, native) = YahooParser.MinorUnit(cached.Currency);
        var fx = 1m;
        if (native.Length > 0 && native != currency)
        {
            var rate = await GetRawQuoteAsync(FxSymbol(native, currency), online, force, cancellationToken);
            if (rate.Cached is null)
                return LiveQuote.Missing(symbol, QuoteStatus.Unavailable);
            fx = (decimal)rate.Cached.Price;
        }

        var price = Math.Round((decimal)cached.Price * factor * fx, 6);
        decimal? previous = cached.PreviousClose is { } p ? Math.Round((decimal)p * factor * fx, 6) : null;
        return new LiveQuote(symbol, QuoteStatus.Ok, price, previous, cached.AsOf, cached.Name, native);
    }

    private sealed record RawQuote(QuoteStatus Status, CachedQuote? Cached);

    private async Task<RawQuote> GetRawQuoteAsync(string symbol, bool online, bool force, CancellationToken cancellationToken)
    {
        var stored = (await store.GetCachedQuotesAsync([symbol])).GetValueOrDefault(symbol);
        var now = _time.GetUtcNow().UtcDateTime;
        if (!online || (!force && stored is not null && now - stored.FetchedAt < QuoteLifetime))
            return new RawQuote(stored is null ? QuoteStatus.Unavailable : QuoteStatus.Ok, stored);

        try
        {
            var fresh = await source.GetQuoteAsync(symbol, cancellationToken);
            if (fresh is null)
                return new RawQuote(QuoteStatus.NotFound, null);
            var cached = new CachedQuote
            {
                Symbol = symbol,
                Price = (double)fresh.Price,
                PreviousClose = fresh.PreviousClose is { } p ? (double)p : null,
                Currency = fresh.Currency,
                Name = fresh.Name is { Length: > 120 } n ? n[..120] : fresh.Name,
                AsOf = fresh.AsOf.UtcDateTime,
                FetchedAt = now,
            };
            await store.SaveCachedQuoteAsync(cached);
            return new RawQuote(QuoteStatus.Ok, cached);
        }
        catch (QuoteSourceException)
        {
            // Offline or throttled: the last stored quote is better than nothing.
            return new RawQuote(stored is null ? QuoteStatus.Unavailable : QuoteStatus.Ok, stored);
        }
    }

    private async Task<PriceSeries?> GetStoredHistoryAsync(string symbol, DateTime from, bool online, CancellationToken cancellationToken)
    {
        // One download at a time: the evolution chart asks for several symbols and the FX pairs they share.
        await _historyGate.WaitAsync(cancellationToken);
        try
        {
            var coverage = await store.GetPriceCoverageAsync(symbol);
            var now = _time.GetUtcNow().UtcDateTime;
            if (online)
            {
                DateTime? fetchFrom = null;
                if (coverage is null || from.Date < coverage.CoveredFrom)
                    fetchFrom = from.Date.AddDays(-7);
                else if (now - coverage.FetchedAt > HistoryLifetime)
                    fetchFrom = now.Date.AddDays(-14);

                if (fetchFrom is { } start)
                {
                    try
                    {
                        var fetched = await source.GetDailyHistoryAsync(symbol, start, now.Date, cancellationToken);
                        if (fetched is not null)
                        {
                            await store.SavePriceHistoryAsync(symbol, fetched.Currency, start, now, fetched.Closes);
                            coverage = await store.GetPriceCoverageAsync(symbol);
                        }
                    }
                    catch (QuoteSourceException)
                    {
                        // Keep what is stored.
                    }
                }
            }

            if (coverage is null)
                return null;
            return new PriceSeries(symbol, coverage.Currency, await store.GetPriceHistoryAsync(symbol, from.Date.AddDays(-7)));
        }
        finally
        {
            _historyGate.Release();
        }
    }

    private static string FxSymbol(string from, string to) => $"{from}{to}=X";
}
