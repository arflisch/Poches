using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Poches.Core.Investments;

/// <summary>Latest known price of a symbol, in its quote currency.</summary>
public sealed record QuoteSnapshot(string Symbol, decimal Price, string Currency, DateTimeOffset AsOf, decimal? PreviousClose, string? Name);

public sealed record DailyClose(DateTime Date, decimal Close);

/// <summary>Daily closes, oldest first, in <see cref="Currency"/>.</summary>
public sealed record PriceSeries(string Symbol, string Currency, IReadOnlyList<DailyClose> Closes);

/// <param name="Kind">Yahoo's raw quote type: ETF, EQUITY, MUTUALFUND, INDEX, CRYPTOCURRENCY.</param>
public sealed record SymbolMatch(string Symbol, string Name, string Exchange, string Kind);

/// <summary>A source of market data: null for an unknown symbol, <see cref="QuoteSourceException"/> when unreachable.</summary>
public interface IQuoteSource
{
    Task<QuoteSnapshot?> GetQuoteAsync(string symbol, CancellationToken cancellationToken);

    Task<PriceSeries?> GetDailyHistoryAsync(string symbol, DateTime from, DateTime to, CancellationToken cancellationToken);

    Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, CancellationToken cancellationToken);
}

public sealed class QuoteSourceException(string message, Exception? inner = null) : Exception(message, inner);

public static partial class Symbols
{
    /// <summary>Yahoo-style symbols only (CW8.PA, AAPL, ^FCHI, USDEUR=X, BTC-EUR): nothing else reaches the network.</summary>
    public static bool IsValid(string? symbol) => symbol is { Length: > 0 and <= 24 } && Pattern().IsMatch(symbol);

    [GeneratedRegex(@"^\^?[A-Za-z0-9][A-Za-z0-9.\-=]*$")]
    private static partial Regex Pattern();
}

public static class DailyCloseExtensions
{
    /// <summary>The last close on or before <paramref name="date"/>, or null when the series starts later.</summary>
    public static decimal? CloseOnOrBefore(this IReadOnlyList<DailyClose> closes, DateTime date)
    {
        int lo = 0, hi = closes.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (closes[mid].Date <= date) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found >= 0 ? closes[found].Close : null;
    }

    /// <summary>The close on or before <paramref name="date"/>, falling back to the first close after it.</summary>
    public static decimal? CloseNear(this IReadOnlyList<DailyClose> closes, DateTime date) =>
        closes.CloseOnOrBefore(date) ?? (closes.Count > 0 ? closes[0].Close : null);
}

/// <summary>
/// Yahoo Finance's public chart and search endpoints: free and keyless, but unofficial. Yahoo may change or
/// throttle them, and their terms restrict commercial use: a store release needs a licensed data provider.
/// </summary>
public sealed class YahooFinanceClient(HttpClient http) : IQuoteSource
{
    private int _primed;

    public async Task<QuoteSnapshot?> GetQuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        var json = await GetAsync($"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=5d&interval=1d", cancellationToken);
        return json is null ? null : YahooParser.ParseChart(json)?.Quote;
    }

    public async Task<PriceSeries?> GetDailyHistoryAsync(string symbol, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(from.Date, TimeSpan.Zero).ToUnixTimeSeconds();
        var end = new DateTimeOffset(to.Date.AddDays(1), TimeSpan.Zero).ToUnixTimeSeconds();
        var json = await GetAsync(string.Create(CultureInfo.InvariantCulture,
            $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={start}&period2={end}&interval=1d"), cancellationToken);
        return json is null ? null : YahooParser.ParseChart(json)?.Series;
    }

    public async Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var json = await GetAsync(
            $"https://query2.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(query)}&quotesCount=10&newsCount=0&listsCount=0",
            cancellationToken);
        return json is null ? [] : YahooParser.ParseSearch(json);
    }

    /// <summary>Body of a successful call, null for an unknown symbol (404).</summary>
    private async Task<string?> GetAsync(string url, CancellationToken cancellationToken, bool retried = false)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new QuoteSourceException("Yahoo Finance cannot be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new QuoteSourceException("Yahoo Finance did not answer in time.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests && !retried
                && Interlocked.Exchange(ref _primed, 1) == 0)
            {
                // fc.yahoo.com answers 404 but sets the consent cookie the API expects; try once more after it.
                try { using var _ = await http.GetAsync("https://fc.yahoo.com/", cancellationToken); }
                catch (HttpRequestException) { }
                return await GetAsync(url, cancellationToken, retried: true);
            }
            if (!response.IsSuccessStatusCode)
                throw new QuoteSourceException($"Yahoo Finance refused the request ({(int)response.StatusCode}).");
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
    }
}

/// <summary>Reads Yahoo Finance chart (v8) and search (v1) payloads.</summary>
public static class YahooParser
{
    public sealed record Chart(QuoteSnapshot Quote, PriceSeries Series);

    private static readonly HashSet<string> SearchableKinds = ["EQUITY", "ETF", "MUTUALFUND", "INDEX", "CRYPTOCURRENCY"];

    /// <summary>Null when Yahoo reports no data for the symbol.</summary>
    public static Chart? ParseChart(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("chart", out var chart)
            || !chart.TryGetProperty("result", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
            return null;

        var result = results[0];
        var meta = result.GetProperty("meta");
        var symbol = Str(meta, "symbol") ?? string.Empty;
        var currency = Str(meta, "currency") ?? string.Empty;
        var offset = meta.TryGetProperty("gmtoffset", out var g) && g.TryGetInt64(out var gmt) ? gmt : 0;

        var closes = new List<DailyClose>();
        if (result.TryGetProperty("timestamp", out var stamps) && stamps.ValueKind == JsonValueKind.Array
            && result.TryGetProperty("indicators", out var indicators)
            && indicators.TryGetProperty("quote", out var quotes) && quotes.GetArrayLength() > 0
            && quotes[0].TryGetProperty("close", out var closeArray))
        {
            var count = Math.Min(stamps.GetArrayLength(), closeArray.GetArrayLength());
            for (var i = 0; i < count; i++)
            {
                if (closeArray[i].ValueKind != JsonValueKind.Number) continue;
                var close = Price(closeArray[i].GetDouble());
                if (close <= 0) continue;
                var date = LocalDate(stamps[i].GetInt64(), offset);
                if (closes.Count > 0 && closes[^1].Date == date) closes[^1] = new DailyClose(date, close);
                else closes.Add(new DailyClose(date, close));
            }
        }

        var hasPrice = meta.TryGetProperty("regularMarketPrice", out var p) && p.ValueKind == JsonValueKind.Number;
        if (!hasPrice && closes.Count == 0)
            return null;
        var price = hasPrice ? Price(p.GetDouble()) : closes[^1].Close;
        var asOf = meta.TryGetProperty("regularMarketTime", out var t) && t.TryGetInt64(out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : new DateTimeOffset(closes[^1].Date, TimeSpan.Zero);
        var asOfDate = LocalDate(asOf.ToUnixTimeSeconds(), offset);
        decimal? previousClose = null;
        for (var i = closes.Count - 1; i >= 0; i--)
        {
            if (closes[i].Date < asOfDate) { previousClose = closes[i].Close; break; }
        }

        return new Chart(
            new QuoteSnapshot(symbol, price, currency, asOf, previousClose, Str(meta, "longName") ?? Str(meta, "shortName")),
            new PriceSeries(symbol, currency, closes));
    }

    public static IReadOnlyList<SymbolMatch> ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("quotes", out var quotes) || quotes.ValueKind != JsonValueKind.Array)
            return [];

        var matches = new List<SymbolMatch>();
        foreach (var q in quotes.EnumerateArray())
        {
            var symbol = Str(q, "symbol");
            var kind = Str(q, "quoteType");
            if (symbol is null || kind is null || !SearchableKinds.Contains(kind) || !Symbols.IsValid(symbol)) continue;
            matches.Add(new SymbolMatch(symbol, Str(q, "longname") ?? Str(q, "shortname") ?? symbol, Str(q, "exchDisp") ?? Str(q, "exchange") ?? string.Empty, kind));
        }
        return matches;
    }

    /// <summary>Some exchanges quote in minor units: London in pence (GBp), Johannesburg in cents (ZAc).</summary>
    public static (decimal Factor, string Currency) MinorUnit(string currency) => currency switch
    {
        "GBp" or "GBX" => (0.01m, "GBP"),
        "ZAc" or "ZAC" => (0.01m, "ZAR"),
        "ILA" => (0.01m, "ILS"),
        _ => (1m, currency.ToUpperInvariant()),
    };

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime LocalDate(long unixSeconds, long gmtOffsetSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds + gmtOffsetSeconds).UtcDateTime.Date;

    // Yahoo's closes are single-precision values widened to double (709.5499877929688): going through float
    // keeps seven significant digits, which gives back the quoted price (709.55).
    private static decimal Price(double value) => (decimal)(float)value;
}
