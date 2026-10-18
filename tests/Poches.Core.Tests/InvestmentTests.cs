using Poches.Core.Backup;
using Poches.Core.Data;
using Poches.Core.Investments;
using Poches.Core.Models;

namespace Poches.Core.Tests;

public sealed class InvestmentMathTests
{
    private static readonly DateTime Today = new(2026, 10, 16);

    private static Holding Etf(int id = 1, decimal? manual = null) =>
        new() { Id = id, Name = "ETF S&P 500", Symbol = "PSP5.PA", ManualPrice = manual, ManualPriceDate = manual is null ? null : Today };

    private static Purchase Buy(int id, int holdingId, DateTime date, decimal quantity, decimal price, decimal fees = 0) =>
        new() { Id = id, HoldingId = holdingId, Date = date, Quantity = quantity, UnitPrice = price, Fees = fees };

    [Fact]
    public void Each_purchase_is_measured_from_its_own_date_and_price()
    {
        Purchase[] purchases =
        [
            Buy(1, 1, new DateTime(2026, 1, 15), 20m, 50m, 1.99m),
            Buy(2, 1, new DateTime(2026, 6, 10), 10m, 48.10m),
        ];
        var prices = new Dictionary<int, MarketPrice> { [1] = new(55.20m, 55m, Today) };

        var result = InvestmentMath.Compute([Etf()], purchases, prices, Today);

        var january = result.Purchases.First();
        Assert.Equal(1001.99m, january.Invested);
        Assert.Equal(1104m, january.Value);
        Assert.Equal(0.1018, january.Return, 4);
        Assert.Equal(274, january.Days);
        Assert.Equal(0.1379, january.Annualized!.Value, 3);

        var june = result.Purchases.Last();
        Assert.Equal(0.1476, june.Return, 4);
        Assert.Equal(128, june.Days);

        var holding = Assert.Single(result.Holdings);
        Assert.Equal(30m, holding.Quantity);
        Assert.Equal((1001.99m + 481m) / 30m, holding.AverageCost);
        Assert.Equal(PriceSource.Quote, holding.Source);
        Assert.Equal(30m * 0.20m, holding.DayChange);
        Assert.Equal(1, holding.Weight);
    }

    [Fact]
    public void Short_holdings_are_not_annualised()
    {
        var result = InvestmentMath.Compute([Etf()], [Buy(1, 1, Today.AddDays(-30), 1m, 50m)],
            new Dictionary<int, MarketPrice> { [1] = new(60m, null, Today) }, Today);

        Assert.Null(result.Purchases.Single().Annualized);
        Assert.Null(result.Xirr);
    }

    [Fact]
    public void Without_a_quote_the_manual_price_then_the_last_purchase_price_is_used()
    {
        var withManual = InvestmentMath.Compute([Etf(manual: 70m)], [Buy(1, 1, Today.AddDays(-200), 2m, 50m)], new Dictionary<int, MarketPrice>(), Today);
        Assert.Equal(PriceSource.Manual, withManual.Holdings[0].Source);
        Assert.Equal(140m, withManual.Value);
        Assert.Null(withManual.DayChange);

        var bare = InvestmentMath.Compute([Etf()], [Buy(1, 1, Today.AddDays(-200), 2m, 50m), Buy(2, 1, Today.AddDays(-10), 1m, 55m)], new Dictionary<int, MarketPrice>(), Today);
        Assert.Equal(PriceSource.LastPurchase, bare.Holdings[0].Source);
        Assert.Equal(165m, bare.Value);
    }

    [Fact]
    public void Weights_split_the_total_value()
    {
        Holding[] holdings = [Etf(1, 100m), new Holding { Id = 2, Name = "Fonds euros", ManualPrice = 300m, ManualPriceDate = Today }];
        var result = InvestmentMath.Compute(holdings, [Buy(1, 1, Today.AddDays(-100), 1m, 90m), Buy(2, 2, Today.AddDays(-100), 1m, 290m)],
            new Dictionary<int, MarketPrice>(), Today);

        Assert.Equal(["Fonds euros", "ETF S&P 500"], result.Holdings.Select(h => h.Holding.Name));
        Assert.Equal(0.75, result.Holdings[0].Weight, 6);
        Assert.Equal(0.25, result.Holdings[1].Weight, 6);
    }

    [Fact]
    public void Xirr_of_a_single_flow_matches_the_compound_rate()
    {
        var start = new DateTime(2025, 1, 1);
        var rate = InvestmentMath.Xirr([(start, -1000m), (start.AddDays(365), 1100m)]);
        Assert.Equal(0.10, rate!.Value, 4);
    }

    [Fact]
    public void Evolution_compares_with_the_same_cash_in_a_benchmark()
    {
        var d1 = new DateTime(2026, 1, 15);
        var d2 = new DateTime(2026, 6, 10);
        Holding[] holdings = [Etf()];
        Purchase[] purchases = [Buy(1, 1, d1, 10m, 50m), Buy(2, 1, d2, 10m, 55m, 2m)];
        var history = new Dictionary<int, IReadOnlyList<DailyClose>> { [1] = [new(d1, 50m), new(new DateTime(2026, 3, 2), 52m), new(d2, 55m)] };
        IReadOnlyList<DailyClose> bench = [new(d1, 100m), new(d2, 120m)];

        var points = Evolution.Compute(holdings, purchases, history, new Dictionary<int, decimal> { [1] = 61m }, bench, 150m, Today);

        Assert.Equal(d1, points[0].Date);
        Assert.Equal(500m, points[0].Benchmark);
        Assert.Equal(520m, points.First(p => p.Date == new DateTime(2026, 3, 10)).Value);
        Assert.Equal(1052m, points[^1].Invested);
        Assert.Equal(20 * 61m, points[^1].Value);
        Assert.Equal(Math.Round((5m + 552m / 120m) * 150m, 2), points[^1].Benchmark);
    }
}

public sealed class YahooParserTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Chart_gives_the_live_price_previous_close_and_daily_closes()
    {
        var chart = YahooParser.ParseChart(Fixture("yahoo-chart-cw8.json"))!;

        Assert.Equal("CW8.PA", chart.Quote.Symbol);
        Assert.Equal("EUR", chart.Quote.Currency);
        Assert.Equal(709.55m, chart.Quote.Price);
        Assert.Equal(710.53m, chart.Quote.PreviousClose);
        Assert.Equal(22, chart.Series.Closes.Count);
        Assert.Equal(new DateTime(2026, 10, 15), chart.Series.Closes[^1].Date);
    }

    [Fact]
    public void Unknown_symbols_and_search_results_are_read()
    {
        Assert.Null(YahooParser.ParseChart(Fixture("yahoo-chart-not-found.json")));
        var matches = YahooParser.ParseSearch(Fixture("yahoo-search.json"));
        Assert.Equal("CW8.PA", matches[0].Symbol);
        Assert.Equal("ETF", matches[0].Kind);
    }

    [Theory]
    [InlineData("CW8.PA", true)]
    [InlineData("^FCHI", true)]
    [InlineData("USDEUR=X", true)]
    [InlineData("../admin", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("A B", false)]
    public void Only_market_symbols_reach_the_network(string symbol, bool valid) => Assert.Equal(valid, Symbols.IsValid(symbol));
}

public sealed class InvestmentStoreTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"poches-inv-{Guid.NewGuid():N}.db3");
    private BudgetStore _store = null!;

    public Task InitializeAsync()
    {
        _store = new BudgetStore(_path);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + "*"))
            File.Delete(file);
    }

    [Fact]
    public async Task A_first_purchase_creates_its_holding_in_the_same_step()
    {
        var purchase = new Purchase { Date = new DateTime(2026, 1, 15, 14, 30, 0), Quantity = 29.4002m, UnitPrice = 51.02m, Fees = 1.5m };
        await _store.SavePurchaseAsync(purchase, DateTime.Today, new Holding { Name = "  Amundi PEA S&P 500  ", Symbol = "psp5.pa", Code = "fr0011871128" });

        var holding = Assert.Single(await _store.GetHoldingsAsync());
        Assert.Equal("Amundi PEA S&P 500", holding.Name);
        Assert.Equal("PSP5.PA", holding.Symbol);
        Assert.Equal("FR0011871128", holding.Code);
        var stored = Assert.Single(await _store.GetPurchasesAsync(holding.Id));
        Assert.Equal(new DateTime(2026, 1, 15), stored.Date);
        Assert.Equal(29.4002m, stored.Quantity);
        Assert.Equal(1501.50m, decimal.Round(stored.Invested, 2));
    }

    [Theory]
    [InlineData(0, 50, 0, BudgetError.InvalidQuantity)]
    [InlineData(1, 0, 0, BudgetError.InvalidUnitPrice)]
    [InlineData(1, 50, -1, BudgetError.InvalidFees)]
    public async Task Invalid_purchases_are_refused(decimal quantity, decimal price, decimal fees, BudgetError expected)
    {
        var holding = await _store.SaveHoldingAsync(new Holding { Name = "ETF" });
        var ex = await Assert.ThrowsAsync<BudgetException>(() =>
            _store.SavePurchaseAsync(new Purchase { HoldingId = holding.Id, Date = DateTime.Today, Quantity = quantity, UnitPrice = price, Fees = fees }, DateTime.Today));
        Assert.Equal(expected, ex.Error);
    }

    [Fact]
    public async Task Purchases_in_the_future_bad_symbols_and_unknown_holdings_are_refused()
    {
        var holding = await _store.SaveHoldingAsync(new Holding { Name = "ETF" });
        var future = await Assert.ThrowsAsync<BudgetException>(() =>
            _store.SavePurchaseAsync(new Purchase { HoldingId = holding.Id, Date = DateTime.Today.AddDays(1), Quantity = 1, UnitPrice = 1 }, DateTime.Today));
        Assert.Equal(BudgetError.PurchaseInFuture, future.Error);

        var symbol = await Assert.ThrowsAsync<BudgetException>(() => _store.SaveHoldingAsync(new Holding { Name = "X", Symbol = "../hack" }));
        Assert.Equal(BudgetError.InvalidSymbol, symbol.Error);

        var unknown = await Assert.ThrowsAsync<BudgetException>(() =>
            _store.SavePurchaseAsync(new Purchase { HoldingId = 999, Date = DateTime.Today, Quantity = 1, UnitPrice = 1 }, DateTime.Today));
        Assert.Equal(BudgetError.HoldingNotFound, unknown.Error);
    }

    [Fact]
    public async Task Deleting_a_holding_deletes_its_purchases()
    {
        var holding = await _store.SaveHoldingAsync(new Holding { Name = "ETF" });
        await _store.SavePurchaseAsync(new Purchase { HoldingId = holding.Id, Date = DateTime.Today, Quantity = 1, UnitPrice = 1 }, DateTime.Today);

        await _store.DeleteHoldingAsync(holding.Id);

        Assert.Empty(await _store.GetHoldingsAsync());
        Assert.Empty(await _store.GetPurchasesAsync());
    }

    [Fact]
    public async Task Investments_round_trip_through_an_encrypted_backup()
    {
        await _store.SeedSampleInvestmentsAsync(DateTime.Today);
        var exported = await _store.ExportAsync(DateTime.Now);
        var file = BackupSerializer.WriteEncrypted(exported, "mot de passe");

        await _store.DeleteAllAsync();
        Assert.Empty(await _store.GetHoldingsAsync());

        await _store.ImportAsync(BackupSerializer.Read(file, "mot de passe"));
        var holdings = await _store.GetHoldingsAsync();
        Assert.Equal(3, holdings.Count);
        Assert.Contains(holdings, h => h.Symbol == "CW8.PA" && h.ManualPrice == 709.55m);
        Assert.Equal(exported.Purchases.Count, (await _store.GetPurchasesAsync()).Count);
    }

    [Fact]
    public async Task Backups_made_before_investments_leave_them_untouched()
    {
        await _store.SeedSampleInvestmentsAsync(DateTime.Today);
        var old = (await _store.ExportAsync(DateTime.Now)) with { Version = 4, Holdings = [], Purchases = [] };

        await _store.ImportAsync(old);

        Assert.Equal(3, (await _store.GetHoldingsAsync()).Count);
    }

    // As written by Poches 1.0 (build 2): no "holdings" nor "purchases" key at all.
    private const string VersionFourFile = """
        {
          "format": "poches-backup",
          "version": 4,
          "exportedAt": "2026-10-02T11:20:00+02:00",
          "currency": "€",
          "pockets": [
            { "id": 3, "name": "Vacances", "icon": "🏖️", "colorHex": "#F97316", "goalCents": 150000, "createdAt": "2026-03-25T21:25:51" }
          ],
          "movements": [
            { "pocketId": 3, "amountCents": 45000, "kind": "Deposit", "note": "Salaire", "date": "2026-09-01T10:00:00", "transferGroup": null, "counterpartPocketId": null }
          ],
          "subscriptions": [
            { "name": "Netflix", "icon": "📺", "colorHex": "#E11D48", "amountCents": 1399, "period": "Monthly", "billingAnchor": "2026-09-12T00:00:00", "isActive": true, "createdAt": "2026-09-01T10:00:00", "category": "Subscription" }
          ],
          "scheduledDeposits": [
            { "pocketId": 3, "amountCents": 10000, "period": "Monthly", "anchor": "2026-10-01T00:00:00", "appliedThrough": "2026-10-01T00:00:00", "note": null, "isActive": true, "createdAt": "2026-09-01T10:00:00" }
          ],
          "splitRules": [ { "pocketId": 3, "basisPoints": 1000 } ]
        }
        """;

    [Fact]
    public async Task A_version_4_file_without_investments_is_read_and_restored()
    {
        var document = BackupSerializer.Read(System.Text.Encoding.UTF8.GetBytes(VersionFourFile));

        Assert.Equal(4, document.Version);
        Assert.Empty(document.Holdings);
        Assert.Empty(document.Purchases);

        await _store.ImportAsync(document);
        Assert.Equal("Vacances", Assert.Single(await _store.GetPocketSummariesAsync()).Pocket.Name);
        Assert.Empty(await _store.GetHoldingsAsync());
    }

    [Fact]
    public async Task Restoring_a_version_4_file_keeps_the_investments_and_the_next_backup_uses_the_new_format()
    {
        await _store.SeedSampleInvestmentsAsync(DateTime.Today);
        var purchases = (await _store.GetPurchasesAsync()).Count;

        await _store.ImportAsync(BackupSerializer.Read(System.Text.Encoding.UTF8.GetBytes(VersionFourFile)));
        Assert.Equal(3, (await _store.GetHoldingsAsync()).Count);
        Assert.Equal(purchases, (await _store.GetPurchasesAsync()).Count);

        var next = await _store.ExportAsync(DateTime.Now);
        Assert.Equal(BackupDocument.CurrentVersion, next.Version);
        var reread = BackupSerializer.Read(BackupSerializer.WriteEncrypted(next, "mot de passe"), "mot de passe");
        Assert.Equal(5, reread.Version);
        Assert.Equal(3, reread.Holdings.Count);
        Assert.Equal(purchases, reread.Purchases.Count);
        Assert.Equal("Vacances", Assert.Single(reread.Pockets).Name);
    }

    [Fact]
    public async Task Null_investment_lists_in_a_version_5_file_count_as_empty()
    {
        const string file = """
            { "format": "poches-backup", "version": 5, "exportedAt": "2026-10-18T09:00:00", "currency": "€",
              "pockets": [], "movements": [], "holdings": null, "purchases": null }
            """;

        var document = BackupSerializer.Read(System.Text.Encoding.UTF8.GetBytes(file));
        Assert.Empty(document.Holdings);
        Assert.Empty(document.Purchases);

        await _store.ImportAsync(document);
        Assert.Empty(await _store.GetHoldingsAsync());
    }

    [Fact]
    public async Task A_purchase_pointing_to_an_unknown_holding_makes_the_backup_corrupt()
    {
        var bad = new BackupDocument { Purchases = [new BackupPurchase(42, DateTime.Today, 1_000_000, 1_000_000, 0)] };
        var ex = await Assert.ThrowsAsync<BudgetException>(() => _store.ImportAsync(bad));
        Assert.Equal(BudgetError.CorruptBackup, ex.Error);
    }

    [Fact]
    public async Task Erasing_everything_also_forgets_the_market_data()
    {
        await _store.SaveCachedQuoteAsync(new CachedQuote { Symbol = "CW8.PA", Price = 700, Currency = "EUR", AsOf = DateTime.UtcNow, FetchedAt = DateTime.UtcNow });
        await _store.SavePriceHistoryAsync("CW8.PA", "EUR", DateTime.Today.AddDays(-10), DateTime.UtcNow, [new DailyClose(DateTime.Today, 700m)]);

        await _store.DeleteAllAsync();

        Assert.Empty(await _store.GetCachedQuotesAsync(["CW8.PA"]));
        Assert.Null(await _store.GetPriceCoverageAsync("CW8.PA"));
    }
}

public sealed class MarketDataTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"poches-md-{Guid.NewGuid():N}.db3");
    private BudgetStore _store = null!;

    public Task InitializeAsync()
    {
        _store = new BudgetStore(_path);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + "*"))
            File.Delete(file);
    }

    private sealed class FakeSource : IQuoteSource
    {
        public int QuoteCalls;
        public bool Offline;

        private static readonly Dictionary<string, (decimal Price, string Currency)> Prices = new()
        {
            ["PSP5.PA"] = (60m, "EUR"),
            ["VUSA.L"] = (11000m, "GBp"),
            ["GBPEUR=X"] = (1.2m, "EUR"),
        };

        public Task<QuoteSnapshot?> GetQuoteAsync(string symbol, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref QuoteCalls);
            if (Offline) throw new QuoteSourceException("offline");
            return Task.FromResult(Prices.TryGetValue(symbol, out var p)
                ? new QuoteSnapshot(symbol, p.Price, p.Currency, DateTimeOffset.UtcNow, p.Price * 0.99m, symbol)
                : null);
        }

        public Task<PriceSeries?> GetDailyHistoryAsync(string symbol, DateTime from, DateTime to, CancellationToken cancellationToken)
        {
            if (Offline) throw new QuoteSourceException("offline");
            if (!Prices.TryGetValue(symbol, out var p)) return Task.FromResult<PriceSeries?>(null);
            var closes = new List<DailyClose>();
            for (var d = from.Date; d <= to.Date; d = d.AddDays(1)) closes.Add(new DailyClose(d, p.Price));
            return Task.FromResult<PriceSeries?>(new PriceSeries(symbol, p.Currency, closes));
        }

        public Task<IReadOnlyList<SymbolMatch>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SymbolMatch>>([]);
    }

    [Fact]
    public async Task Quotes_are_converted_cached_and_kept_for_offline_use()
    {
        var source = new FakeSource();
        var market = new MarketData(_store, source);

        var quotes = await market.GetQuotesAsync(["PSP5.PA", "VUSA.L", "NOPE.PA"], "EUR", online: true, force: false, CancellationToken.None);
        Assert.Equal(60m, quotes["PSP5.PA"].Price);
        Assert.Equal(132m, quotes["VUSA.L"].Price); // 11 000 pence × 1.2
        Assert.Equal(QuoteStatus.NotFound, quotes["NOPE.PA"].Status);

        var calls = source.QuoteCalls;
        await market.GetQuotesAsync(["PSP5.PA"], "EUR", online: true, force: false, CancellationToken.None);
        Assert.Equal(calls, source.QuoteCalls); // fresh enough: served from the cache

        source.Offline = true;
        var offline = await market.GetQuotesAsync(["PSP5.PA"], "EUR", online: true, force: true, CancellationToken.None);
        Assert.Equal(60m, offline["PSP5.PA"].Price);

        var disabled = await market.GetQuotesAsync(["PSP5.PA"], "EUR", online: false, force: false, CancellationToken.None);
        Assert.Equal(60m, disabled["PSP5.PA"].Price);
    }

    [Fact]
    public async Task Nothing_is_fetched_while_online_quotes_are_off()
    {
        var source = new FakeSource();
        var market = new MarketData(_store, source);

        var quotes = await market.GetQuotesAsync(["PSP5.PA"], "EUR", online: false, force: true, CancellationToken.None);
        var history = await market.GetHistoryAsync("PSP5.PA", DateTime.Today.AddDays(-30), "EUR", online: false, CancellationToken.None);

        Assert.Equal(0, source.QuoteCalls);
        Assert.Equal(QuoteStatus.Unavailable, quotes["PSP5.PA"].Status);
        Assert.Null(history);
    }

    [Fact]
    public async Task History_in_pence_is_converted_with_the_daily_rate()
    {
        var market = new MarketData(_store, new FakeSource());
        var history = await market.GetHistoryAsync("VUSA.L", DateTime.Today.AddDays(-20), "EUR", online: true, CancellationToken.None);

        Assert.NotNull(history);
        Assert.All(history, c => Assert.Equal(132m, c.Close));
    }
}
