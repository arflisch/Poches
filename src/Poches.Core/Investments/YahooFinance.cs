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

/// <summary>Distributions of a symbol, oldest first, per unit in <see cref="Currency"/> (the listing's currency).</summary>
public sealed record DividendSeries(string Symbol, string Currency, IReadOnlyList<DividendEvent> Events);

/// <summary>
/// What an instrument holds, as Yahoo describes it: for a fund, its sector weights (within its shares) and its split
/// between shares, bonds and cash; for a share, its sector and country. <see cref="Ter"/> is a fund's yearly running cost.
/// </summary>
public sealed record InstrumentProfile(
    string Symbol,
    string Kind,
    string? Name,
    IReadOnlyDictionary<string, decimal> Sectors,
    decimal? Stocks,
    decimal? Bonds,
    decimal? Cash,
    decimal? Other,
    string? Country,
    decimal? Ter);

/// <summary>A source of market data: null for an unknown symbol, <see cref="QuoteSourceException"/> when unreachable.</summary>
public interface IQuoteSource
{
    Task<QuoteSnapshot?> GetQuoteAsync(string symbol, CancellationToken cancellationToken);

    Task<PriceSeries?> GetDailyHistoryAsync(string symbol, DateTime from, DateTime to, CancellationToken cancellationToken);

    Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>Distributions between two dates; null when unknown.</summary>
    Task<DividendSeries?> GetDividendsAsync(string symbol, DateTime from, DateTime to, CancellationToken cancellationToken) =>
        Task.FromResult<DividendSeries?>(null);

    /// <summary>Holdings breakdown and running costs; null when unknown.</summary>
    Task<InstrumentProfile?> GetProfileAsync(string symbol, CancellationToken cancellationToken) =>
        Task.FromResult<InstrumentProfile?>(null);
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
    private readonly SemaphoreSlim _crumbGate = new(1, 1);
    private string? _crumb;
    private DateTime _crumbAt;

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

    public async Task<DividendSeries?> GetDividendsAsync(string symbol, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(from.Date, TimeSpan.Zero).ToUnixTimeSeconds();
        var end = new DateTimeOffset(to.Date.AddDays(1), TimeSpan.Zero).ToUnixTimeSeconds();
        // Monthly bars keep the answer small: only the events are wanted.
        var json = await GetAsync(string.Create(CultureInfo.InvariantCulture,
            $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={start}&period2={end}&interval=1mo&events=div"), cancellationToken);
        return json is null ? null : YahooParser.ParseDividends(json);
    }

    public async Task<InstrumentProfile?> GetProfileAsync(string symbol, CancellationToken cancellationToken)
    {
        // The summary endpoint needs a session token ("crumb"); a stale one is refused, so fetch a fresh one once.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var crumb = await GetCrumbAsync(attempt > 0, cancellationToken) ?? throw new QuoteSourceException("Yahoo Finance gave no session token.");
            try
            {
                var json = await GetAsync(
                    $"https://query1.finance.yahoo.com/v10/finance/quoteSummary/{Uri.EscapeDataString(symbol)}?modules=topHoldings,fundProfile,assetProfile,price&crumb={Uri.EscapeDataString(crumb)}",
                    cancellationToken);
                return json is null ? null : YahooParser.ParseProfile(json);
            }
            catch (QuoteSourceException) when (attempt == 0)
            {
            }
        }
        return null;
    }

    private async Task<string?> GetCrumbAsync(bool renew, CancellationToken cancellationToken)
    {
        await _crumbGate.WaitAsync(cancellationToken);
        try
        {
            if (!renew && _crumb is not null && DateTime.UtcNow - _crumbAt < TimeSpan.FromHours(6))
                return _crumb;
            // fc.yahoo.com answers 404 but sets the consent cookie the token needs.
            try { using var _ = await http.GetAsync("https://fc.yahoo.com/", cancellationToken); }
            catch (HttpRequestException) { }
            // The token is plain text: a JSON Accept header gets it refused.
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://query2.finance.yahoo.com/v1/test/getcrumb");
            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd("*/*");
            using var response = await http.SendAsync(request, cancellationToken);
            var text = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (!response.IsSuccessStatusCode || text.Length is 0 or > 64 || text.Contains(' '))
                return null;
            (_crumb, _crumbAt) = (text, DateTime.UtcNow);
            return _crumb;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        finally
        {
            _crumbGate.Release();
        }
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

    /// <summary>The "events.dividends" of a chart answer (asked with events=div); null when Yahoo has no data.</summary>
    public static DividendSeries? ParseDividends(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("chart", out var chart)
            || !chart.TryGetProperty("result", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
            return null;
        var result = results[0];
        var meta = result.GetProperty("meta");
        var symbol = Str(meta, "symbol") ?? "";
        var currency = Str(meta, "currency") ?? "";
        var offset = meta.TryGetProperty("gmtoffset", out var g) && g.TryGetInt64(out var gmt) ? gmt : 0;
        var events = new List<DividendEvent>();
        if (result.TryGetProperty("events", out var all) && all.TryGetProperty("dividends", out var dividends) && dividends.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in dividends.EnumerateObject())
            {
                if (entry.Value.TryGetProperty("date", out var date) && date.TryGetInt64(out var seconds)
                    && entry.Value.TryGetProperty("amount", out var amount) && amount.ValueKind == JsonValueKind.Number && Price(amount.GetDouble()) is > 0 and var value)
                    events.Add(new DividendEvent(LocalDate(seconds, offset), value));
            }
        }
        return new DividendSeries(symbol, currency, events.OrderBy(e => e.Date).ToList());
    }

    /// <summary>A quoteSummary answer (topHoldings, fundProfile, assetProfile, price); null when Yahoo has no data.</summary>
    public static InstrumentProfile? ParseProfile(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("quoteSummary", out var summary)
            || !summary.TryGetProperty("result", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
            return null;
        var result = results[0];
        string symbol = "", kind = "", name = "";
        if (result.TryGetProperty("price", out var price))
        {
            symbol = Str(price, "symbol") ?? "";
            kind = Str(price, "quoteType") ?? "";
            name = Str(price, "longName") ?? Str(price, "shortName") ?? "";
        }
        var sectors = new Dictionary<string, decimal>();
        decimal? stocks = null, bonds = null, cash = null, other = null;
        if (result.TryGetProperty("topHoldings", out var holdings))
        {
            if (holdings.TryGetProperty("sectorWeightings", out var weights) && weights.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in weights.EnumerateArray())
                foreach (var sector in entry.EnumerateObject())
                    if (Raw(sector.Value) is { } weight and > 0)
                        sectors[SectorKey(sector.Name)] = sectors.GetValueOrDefault(SectorKey(sector.Name)) + weight;
            }
            stocks = Raw(holdings, "stockPosition");
            bonds = Raw(holdings, "bondPosition");
            cash = Raw(holdings, "cashPosition");
            other = (Raw(holdings, "otherPosition") ?? 0) + (Raw(holdings, "preferredPosition") ?? 0) + (Raw(holdings, "convertiblePosition") ?? 0);
        }
        string? country = null;
        if (result.TryGetProperty("assetProfile", out var profile))
        {
            country = Str(profile, "country");
            if (sectors.Count == 0 && (Str(profile, "sectorKey") ?? Str(profile, "sector")) is { Length: > 0 } sector)
                sectors[SectorKey(sector)] = 1m;
        }
        decimal? ter = null;
        if (result.TryGetProperty("fundProfile", out var fund) && fund.TryGetProperty("feesExpensesInvestment", out var fees))
            ter = Raw(fees, "annualReportExpenseRatio");
        return new InstrumentProfile(symbol, kind, name.Length > 0 ? name : null, sectors, stocks, bonds, cash, other is > 0 ? other : 0, country, ter is > 0 ? ter : null);
    }

    /// <summary>One spelling for both sources: "consumer_cyclical" (funds), "consumer-cyclical" or "Consumer Cyclical" (shares).</summary>
    private static string SectorKey(string name)
    {
        var key = name.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return key == "real_estate" ? "realestate" : key;
    }

    private static decimal? Raw(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) ? Raw(value) : null;

    private static decimal? Raw(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("raw", out var raw) && raw.TryGetDouble(out var number) ? Math.Round((decimal)number, 6) : null;

    private static DateTime LocalDate(long unixSeconds, long gmtOffsetSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds + gmtOffsetSeconds).UtcDateTime.Date;

    // Yahoo's closes are single-precision values widened to double (709.5499877929688): going through float
    // keeps seven significant digits, which gives back the quoted price (709.55).
    private static decimal Price(double value) => (decimal)(float)value;
}
