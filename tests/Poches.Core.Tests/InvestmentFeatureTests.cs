using Poches.Core.Backup;
using Poches.Core.Data;
using Poches.Core.Import;
using Poches.Core.Investments;
using Poches.Core.Models;

namespace Poches.Core.Tests;

/// <summary>Sales, dividends, currencies, Belgian tax, plan and analysis: the same rules as the web carnet.</summary>
public sealed class InvestmentFeatureTests
{
    private static readonly DateTime Today = new(2026, 10, 16);

    private static Holding Line(int id = 1, HoldingType type = HoldingType.Stock, TaxEnvelope envelope = TaxEnvelope.Standard) =>
        new() { Id = id, Name = $"Line {id}", Type = type, Envelope = envelope };

    private static Purchase Buy(int id, DateTime date, decimal qty, decimal price, decimal fees = 0, decimal tob = 0, int holding = 1) =>
        new() { Id = id, HoldingId = holding, Date = date, Quantity = qty, UnitPrice = price, Fees = fees, Tob = tob };

    private static Sale Sell(int id, DateTime date, decimal qty, decimal price, decimal fees = 0, decimal tob = 0, int holding = 1) =>
        new() { Id = id, HoldingId = holding, Date = date, Quantity = qty, UnitPrice = price, Fees = fees, Tob = tob };

    private static Dictionary<int, MarketPrice> Price(decimal price, int holding = 1) => new() { [holding] = new MarketPrice(price, null, Today) };

    [Fact]
    public void A_sale_takes_the_oldest_units_first_and_realizes_the_gain()
    {
        Purchase[] lots = [Buy(1, new(2024, 9, 10), 100, 15, 5, 5), Buy(2, new(2026, 2, 1), 50, 20)];
        Sale[] sales = [Sell(1, new(2026, 6, 1), 120, 25, 10)];

        var result = InvestmentMath.Compute([Line()], lots, Price(22), Today, sales);

        var h = Assert.Single(result.Holdings);
        Assert.Equal(30, h.Quantity);
        var first = h.Purchases[0];
        Assert.True(first.SoldOut);
        Assert.Null(first.Annualized);
        var second = h.Purchases[1];
        Assert.Equal((30m, 20m), (second.RemainingQuantity, second.SoldQuantity));
        Assert.Equal(30 * 20m, second.Invested);
        // 100 units cost 1510, 20 more cost 400; 120 × 25 − 10 received.
        Assert.Equal(2990m - 1510m - 400m, h.Realized, 6);
        Assert.Equal(h.Realized, result.Realized);
        Assert.Equal(30 * 22m - 600m, result.Gain);
    }

    [Fact]
    public void A_fully_sold_line_is_closed_and_dividends_count_in_its_total_gain()
    {
        var dividend = new Dividend { HoldingId = 1, Date = new(2025, 5, 2), Gross = 148, Withholding = 44.40m };
        var result = InvestmentMath.Compute([Line()], [Buy(1, new(2024, 9, 10), 100, 15)], Price(22), Today,
            [Sell(1, new(2026, 6, 1), 100, 20)], [dividend]);

        var h = Assert.Single(result.Holdings);
        Assert.True(h.Closed);
        Assert.Empty(result.Open);
        Assert.Equal(103.60m, h.Dividends);
        Assert.Equal(500m + 103.60m, h.TotalGain);
        Assert.Equal(0, result.Value);
        Assert.NotNull(result.Xirr);
    }

    [Fact]
    public void Operations_in_another_currency_are_converted_at_their_rate()
    {
        // 1 EUR = 1.10 USD: 10 units at 110 USD plus 11 USD of fees cost 1111 USD, so 1010 EUR.
        var buy = Buy(1, new(2026, 3, 2), 10, 110, 11);
        (buy.Currency, buy.FxRateMicros) = ("USD", 1_100_000);
        var dividend = new Dividend { HoldingId = 1, Date = new(2026, 6, 1), Gross = 22, Withholding = 6.60m, Currency = "USD", FxRateMicros = 1_100_000 };

        Assert.Equal(100m, buy.BaseUnitPrice);
        Assert.Equal(1010m, buy.Invested);
        Assert.Equal(14m, dividend.Net);
        var result = InvestmentMath.Compute([Line(type: HoldingType.Etf)], [buy], new Dictionary<int, MarketPrice>(), Today, null, [dividend]);
        Assert.Equal(100m, Assert.Single(result.Holdings).Price);
    }

    [Fact]
    public void The_stock_exchange_tax_is_capped_and_sheltered_envelopes_pay_none()
    {
        Assert.Equal(12m, BelgianTax.Tob(BelgianTax.TobRateFor(Line(type: HoldingType.Etf)), 10_000));
        Assert.Equal(1600m, BelgianTax.Tob(0.0035m, 1_000_000));
        Assert.Equal(0m, BelgianTax.TobRateFor(Line(type: HoldingType.Etf, envelope: TaxEnvelope.PensionSavings)));
        Assert.Equal(0.0132m, BelgianTax.TobRateFor(new Holding { Type = HoldingType.Etf, TobRate = 0.0132m }));
        Assert.Equal(0m, BelgianTax.DefaultTobRate(HoldingType.Crypto));
    }

    [Fact]
    public void Gains_from_2026_use_the_31_december_2025_value_for_older_units_and_the_exemption()
    {
        var settings = new InvestmentSettings();
        Purchase[] lots = [Buy(1, new(2024, 9, 10), 100, 15), Buy(2, new(2026, 2, 1), 50, 20)];
        Sale[] sales = [Sell(1, new(2026, 6, 1), 120, 25)];
        var perf = InvestmentMath.Compute([Line()], lots, Price(22), Today, sales, null, new Dictionary<int, decimal> { [1] = 18 });

        var report = TaxReport.Compute(perf, lots, sales, [], settings, 2026, 2026);

        // 100 units bought at 15 before 2026: basis 18 (31/12/2025), 20 units bought at 20 in 2026.
        Assert.Equal(100 * (25 - 18m) + 20 * (25 - 20m), report.NetGain);
        Assert.Equal(0, report.Taxable);
        Assert.Equal(10_000m - 800m, report.ExemptionLeft);
        Assert.Equal(30 * (22 - 20m), report.LatentGain);
        Assert.Empty(report.MissingReference);

        var before = TaxReport.Compute(perf, lots, sales, [], settings, 2025, 2026);
        Assert.False(before.Applies);
        Assert.Equal(0, before.NetGain);

        var sheltered = InvestmentMath.Compute([Line(envelope: TaxEnvelope.Branch23)], lots, Price(22), Today, sales);
        Assert.Equal(0, TaxReport.Compute(sheltered, lots, sales, [], settings, 2026, 2026).NetGain);
    }

    [Fact]
    public void The_evolution_follows_sales_and_the_period_return_ignores_money_put_in()
    {
        Purchase[] lots = [Buy(1, Today.AddDays(-200), 10, 100), Buy(2, Today.AddDays(-100), 10, 100)];
        Sale[] sales = [Sell(1, Today.AddDays(-50), 5, 100)];
        var history = new Dictionary<int, IReadOnlyList<DailyClose>> { [1] = [new DailyClose(Today.AddDays(-300), 100m)] };

        var points = Evolution.Compute([Line()], lots, history, new Dictionary<int, decimal> { [1] = 100m }, null, null, Today, sales);

        Assert.Equal(1500m, points[^1].Value);
        Assert.Equal(1500m, points[^1].Invested);
        Assert.Equal(-500m, points.Single(p => p.Date == Today.AddDays(-50)).Flow);
        var all = Evolution.Periods(points, null, Today).Single(p => p.Key == "all");
        Assert.Equal(0, all.Twr, 9);
        Assert.Equal(0m, all.Gain);
    }

    [Fact]
    public void Dividends_left_to_reinvest_and_missing_distributions()
    {
        Dividend Div(DateTime date, decimal gross, decimal withheld) => new() { HoldingId = 1, Date = date, Gross = gross, Withholding = withheld };
        Dividend[] divs = [Div(new(2026, 1, 1), 100, 0), Div(new(2026, 6, 1), 70, 20)];
        Purchase[] paid = [new() { HoldingId = 1, Date = new(2026, 3, 1), Quantity = 1, UnitPrice = 100, FromDividends = 80 }];
        Assert.Equal(0, DividendTools.Room(divs, paid, new(2025, 12, 1)));
        Assert.Equal(20, DividendTools.Room(divs, paid, new(2026, 2, 1)));
        Assert.Equal(70, DividendTools.Room(divs, paid, new(2026, 7, 1)));

        DividendEvent[] events = [new(new(2026, 3, 19), 0.5m), new(new(2026, 6, 18), 0.6m), new(new(2026, 9, 17), 0.4m), new(new(2026, 12, 17), 0.5m)];
        Purchase[] lots = [Buy(1, new(2026, 1, 10), 100, 1), Buy(2, new(2026, 6, 18), 50, 1)];
        Sale[] sales = [Sell(1, new(2026, 9, 1), 30, 1)];
        var missing = DividendTools.Missing(lots, sales, [Div(new(2026, 4, 2), 50, 0)], events, new(2026, 10, 19));
        Assert.Equal([new DateTime(2026, 6, 18), new DateTime(2026, 9, 17)], missing.Select(m => m.Date));
        Assert.Equal(100, missing[0].Units);
        Assert.Equal(120, missing[1].Units);
        Assert.Equal(48, missing[1].Gross);
    }

    [Fact]
    public void Rates_are_taken_on_or_before_the_date_and_turned_around_when_inverted()
    {
        DailyClose[] rates = [new(new(2026, 3, 12), 1.08m), new(new(2026, 3, 13), 1.09m), new(new(2026, 3, 16), 1.1m)];
        Assert.Equal(1.09m, FxTools.RateOn(rates, new(2026, 3, 15)));
        Assert.Equal(1.08m, FxTools.RateOn(rates, new(2026, 3, 1)));
        Assert.Equal(1.085m, FxTools.OrientRate(1.085m, 1.09m));
        Assert.Equal(1 / 0.92m, FxTools.OrientRate(0.92m, 1.09m));
    }

    [Fact]
    public void The_plan_splits_a_contribution_and_projects_like_the_web_carnet()
    {
        var parts = Planning.NextContribution([new("w", 7000, 0.8m), new("e", 3000, 0.2m)], 1000);
        Assert.Equal([("w", 1000m)], parts.Select(p => (p.Id, Math.Round(p.Amount, 6))));
        // Same seeded paths as the web page: same percentiles.
        var p = Planning.Project(10000, 300, 120, 0.05, 0.15, 60000);
        Assert.Equal(58706.96823619502, p.Bands[120].P50, 6);
        Assert.Equal(0.4745, p.Probability);
        Assert.Equal(1000, Planning.RequiredMonthly(0, 12000, 12, 0, 0)!.Value, 6);
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Yahoo_dividends_and_fund_profiles_are_read()
    {
        var dividends = YahooParser.ParseDividends(Fixture("yahoo-chart-dividends-vusa.json"));
        Assert.NotNull(dividends);
        Assert.Equal(("VUSA.AS", "EUR"), (dividends.Symbol, dividends.Currency));
        Assert.Equal(new DividendEvent(new DateTime(2024, 3, 15), 0.277751m), dividends.Events[0]);

        var fund = YahooParser.ParseProfile(Fixture("yahoo-summary-iwda.json"));
        Assert.NotNull(fund);
        Assert.Equal("ETF", fund.Kind);
        Assert.Contains("technology", fund.Sectors.Keys);
        Assert.True(fund.Stocks > 0.99m);
        var share = YahooParser.ParseProfile(Fixture("yahoo-summary-engi.json"));
        Assert.Equal(("EQUITY", "France"), (share!.Kind, share.Country));
    }

    [Fact]
    public void Exposure_spreads_funds_over_their_sectors_and_sums_running_costs()
    {
        var fund = new Holding { Id = 1, Name = "World", Type = HoldingType.Etf, Symbol = "IWDA.AS" };
        var share = new Holding { Id = 2, Name = "Engie", Type = HoldingType.Stock, Symbol = "ENGI.PA" };
        var perf = InvestmentMath.Compute([fund, share], [Buy(1, Today.AddDays(-10), 10, 100, holding: 1), Buy(2, Today.AddDays(-10), 10, 100, holding: 2)],
            new Dictionary<int, MarketPrice> { [1] = new(100, null, Today), [2] = new(100, null, Today) }, Today);
        var profiles = new Dictionary<int, InstrumentProfile>
        {
            [1] = new("IWDA.AS", "ETF", null, new Dictionary<string, decimal> { ["technology"] = 0.6m, ["healthcare"] = 0.4m }, 0.9m, 0.1m, 0, 0, null, 0.002m),
            [2] = new("ENGI.PA", "EQUITY", null, new Dictionary<string, decimal> { ["utilities"] = 1 }, null, null, null, null, "France", null),
        };

        var x = ExposureReport.Compute(perf, profiles, new Dictionary<int, string> { [1] = "EUR", [2] = "EUR" }, "EUR");

        Assert.Equal("utilities", x.BySector[0].Key);
        Assert.Equal(1000m * 0.9m * 0.6m, x.BySector.Single(s => s.Key == "technology").Value);
        Assert.Equal(1, x.Coverage, 6);
        Assert.Equal(0.002m, x.WeightedTer);
        Assert.Equal(2m, x.AnnualCost);
        Assert.Contains(x.ByCountry, c => c.Key == "France");
    }

    [Fact]
    public void A_bank_statement_is_read_like_the_web_carnet_reads_it()
    {
        var (_, rows) = BankCsv.Parse("Date;Type;Produit;ISIN;Quantité;Cours;Frais;TOB;Montant;Devise;Cours de change\n05/03/2026;Achat;iShares Core MSCI World;IE00B4L5Y983;10;\"95,50\";14,95;1,15;\"971,15\";EUR;\n14/03/2026;Dividende;Vanguard S&P 500;IE00B3XXRP09;;;;;45,20;USD;1,0850");
        var map = BankCsv.GuessMapping(rows[0]);
        var ops = BankCsv.ToOperations(rows.Skip(1).Cast<IReadOnlyList<string>>().ToList(), map, false);
        Assert.Equal(("buy", 10m, 95.5m, 14.95m, 1.15m), (ops[0].Kind, ops[0].Qty, ops[0].Price, ops[0].Fees, ops[0].Tob));
        Assert.Equal(("dividend", "USD", (decimal?)1.085m, 45.2m), (ops[1].Kind, ops[1].Currency, ops[1].FxRate, ops[1].Gross));
    }

    private static List<CsvOperation> Statement(string csv)
    {
        var (_, rows) = BankCsv.Parse(csv);
        return BankCsv.ToOperations(rows.Skip(1).Cast<IReadOnlyList<string>>().ToList(), BankCsv.GuessMapping(rows[0]), false);
    }

    [Fact]
    public void An_import_matches_lines_skips_what_is_recorded_and_converts_foreign_operations()
    {
        var ops = Statement("Date;Type;Produit;ISIN;Quantité;Cours;Montant;Devise;Cours de change\n"
            + "05/03/2026;Achat;iShares Core MSCI World;IE00B4L5Y983;10;95,50;;EUR;\n"
            + "06/03/2026;Achat;Apple;US0378331005;2;200;;USD;0,92\n"
            + "14/03/2026;Dividende;Vanguard S&P 500;IE00B3XXRP09;;;45,20;USD;\n"
            + "15/03/2026;Vente;Inconnue SA;BE0000000000;1;10;;EUR;");
        Holding[] holdings = [new() { Id = 1, Name = "World", Code = "IE00B4L5Y983", Type = HoldingType.Etf }, new() { Id = 2, Name = "S&P 500", Code = "IE00B3XXRP09" }];
        Purchase[] purchases = [Buy(1, new(2026, 3, 5), 10, 95.5m)];
        var rates = new Dictionary<string, IReadOnlyList<DailyClose>?> { ["USD"] = [new DailyClose(new(2026, 3, 1), 1.08m)] };

        var plan = CsvImportPlanner.Build(ops, holdings, purchases, [], [], "EUR", rates, Today);

        Assert.Equal(CsvRowStatus.Duplicate, plan.Rows[0].Status);
        // 0.92 dollars for a euro is far from the market: the file quotes it the other way round.
        Assert.Equal(("USD", 1 / 0.92m, false), (plan.Rows[1].Currency, plan.Rows[1].FxRate!.Value, plan.Rows[1].RateFromMarket));
        Assert.Equal("US0378331005", Assert.Single(plan.NewHoldings).Isin);
        Assert.Equal((2, "USD", 1.08m, true), (plan.Rows[2].HoldingId!.Value, plan.Rows[2].Currency, plan.Rows[2].FxRate!.Value, plan.Rows[2].RateFromMarket));
        Assert.Equal("unknown_line", plan.Rows[3].Error);
        Assert.Equal((1, 0, 1, 1), (plan.Purchases, plan.Sales, plan.Dividends, plan.Errors));
    }
}

public sealed class InvestmentFeatureStoreTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"poches-inv2-{Guid.NewGuid():N}.db3");
    private BudgetStore _store = null!;
    private static readonly DateTime Today = new(2026, 10, 16);

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

    private async Task<int> SeedAsync()
    {
        var purchase = await _store.SavePurchaseAsync(new Purchase { Date = new(2026, 1, 10), Quantity = 10, UnitPrice = 50, Tob = 0.60m },
            Today, new Holding { Name = "World", Type = HoldingType.Etf });
        return purchase.HoldingId;
    }

    [Fact]
    public async Task A_sale_cannot_sell_more_than_held_and_a_purchase_used_by_a_sale_stays()
    {
        var holding = await SeedAsync();
        var tooMuch = new Sale { HoldingId = holding, Date = new(2026, 3, 1), Quantity = 11, UnitPrice = 55 };
        Assert.Equal(BudgetError.SaleExceedsHoldings, (await Assert.ThrowsAsync<BudgetException>(() => _store.SaveSaleAsync(tooMuch, Today))).Error);
        var early = new Sale { HoldingId = holding, Date = new(2026, 1, 5), Quantity = 1, UnitPrice = 55 };
        Assert.Equal(BudgetError.SaleExceedsHoldings, (await Assert.ThrowsAsync<BudgetException>(() => _store.SaveSaleAsync(early, Today))).Error);

        await _store.SaveSaleAsync(new Sale { HoldingId = holding, Date = new(2026, 3, 1), Quantity = 4, UnitPrice = 55 }, Today);
        var purchase = (await _store.GetPurchasesAsync()).Single();
        Assert.Equal(BudgetError.PurchaseUsedBySale, (await Assert.ThrowsAsync<BudgetException>(() => _store.DeletePurchaseAsync(purchase.Id))).Error);
        purchase.Quantity = 3;
        Assert.Equal(BudgetError.PurchaseUsedBySale, (await Assert.ThrowsAsync<BudgetException>(() => _store.SavePurchaseAsync(purchase, Today))).Error);
    }

    [Fact]
    public async Task Currencies_dividends_and_settings_are_checked()
    {
        var holding = await SeedAsync();
        var noRate = new Dividend { HoldingId = holding, Date = new(2026, 3, 1), Gross = 10, Currency = "USD" };
        Assert.Equal(BudgetError.InvalidFxRate, (await Assert.ThrowsAsync<BudgetException>(() => _store.SaveDividendAsync(noRate, Today))).Error);
        var tooMuchWithheld = new Dividend { HoldingId = holding, Date = new(2026, 3, 1), Gross = 10, Withholding = 11 };
        Assert.Equal(BudgetError.InvalidWithholding, (await Assert.ThrowsAsync<BudgetException>(() => _store.SaveDividendAsync(tooMuchWithheld, Today))).Error);
        var unknown = new Dividend { HoldingId = holding, Date = new(2026, 3, 1), Gross = 10, Currency = "XYZ", FxRateMicros = 1_000_000 };
        Assert.Equal(BudgetError.InvalidCurrency, (await Assert.ThrowsAsync<BudgetException>(() => _store.SaveDividendAsync(unknown, Today))).Error);

        var badGoal = new InvestmentSettings { GoalCents = 100_000_00, GoalDate = Today.AddDays(-1) };
        Assert.Equal(BudgetError.InvalidGoal, (await Assert.ThrowsAsync<BudgetException>(() => _store.SaveInvestmentSettingsAsync(badGoal, Today))).Error);
        await _store.SaveInvestmentSettingsAsync(new InvestmentSettings { MonthlyCents = 300_00, MonthlyDay = 5, GoalCents = 100_000_00, GoalDate = new(2040, 12, 31) }, Today);
        Assert.Equal(300m, (await _store.GetInvestmentSettingsAsync()).Monthly);

        var line = (await _store.GetHoldingAsync(holding))!;
        line.Target = 0.6m;
        await _store.SaveHoldingAsync(line);
        var other = (await _store.SavePurchaseAsync(new Purchase { Date = new(2026, 1, 10), Quantity = 1, UnitPrice = 10 }, Today, new Holding { Name = "Other" })).HoldingId;
        var second = (await _store.GetHoldingAsync(other))!;
        second.Target = 0.5m;
        Assert.Equal(BudgetError.TargetsOverHundredPercent, (await Assert.ThrowsAsync<BudgetException>(() => _store.SaveHoldingAsync(second))).Error);
    }

    [Fact]
    public async Task Deleting_a_line_removes_its_sales_and_dividends()
    {
        var holding = await SeedAsync();
        await _store.SaveSaleAsync(new Sale { HoldingId = holding, Date = new(2026, 3, 1), Quantity = 4, UnitPrice = 55 }, Today);
        await _store.SaveDividendAsync(new Dividend { HoldingId = holding, Date = new(2026, 4, 1), Gross = 10, Withholding = 3 }, Today);

        await _store.DeleteHoldingAsync(holding);

        Assert.Empty(await _store.GetSalesAsync());
        Assert.Empty(await _store.GetDividendsAsync());
    }

    [Fact]
    public async Task A_version_6_backup_keeps_sales_dividends_currencies_and_the_plan()
    {
        var holding = await SeedAsync();
        var line = (await _store.GetHoldingAsync(holding))!;
        (line.Envelope, line.Ter, line.Target, line.ReferencePrice, line.TobRate) = (TaxEnvelope.Standard, 0.002m, 0.5m, 48m, 0.0012m);
        await _store.SaveHoldingAsync(line);
        await _store.SavePurchaseAsync(new Purchase { HoldingId = holding, Date = new(2026, 2, 1), Quantity = 2, UnitPrice = 110, Currency = "USD", FxRateMicros = 1_100_000, FromDividends = 3 }, Today);
        await _store.SaveSaleAsync(new Sale { HoldingId = holding, Date = new(2026, 3, 1), Quantity = 4, UnitPrice = 55, Tob = 0.26m }, Today);
        await _store.SaveDividendAsync(new Dividend { HoldingId = holding, Date = new(2026, 4, 1), Gross = 10, Withholding = 3, Currency = "USD", FxRateMicros = 1_100_000 }, Today);
        await _store.SaveInvestmentSettingsAsync(new InvestmentSettings { MonthlyCents = 300_00, GoalCents = 100_000_00, GoalDate = new(2040, 12, 31), ExemptionCents = 12_000_00 }, Today);

        var file = BackupSerializer.WriteEncrypted(await _store.ExportAsync(DateTime.Now), "pw");
        await _store.DeleteAllAsync();
        await _store.ImportAsync(BackupSerializer.Read(file, "pw"));

        var restored = Assert.Single(await _store.GetHoldingsAsync());
        Assert.Equal((0.002m, 0.5m, 48m, 0.0012m), (restored.Ter!.Value, restored.Target!.Value, restored.ReferencePrice!.Value, restored.TobRate!.Value));
        var usd = (await _store.GetPurchasesAsync()).Single(p => p.Currency is not null);
        Assert.Equal(("USD", 1.1m, 3m), (usd.Currency, usd.Rate, usd.FromDividends));
        Assert.Equal(0.26m, Assert.Single(await _store.GetSalesAsync()).Tob);
        Assert.Equal("USD", Assert.Single(await _store.GetDividendsAsync()).Currency);
        Assert.Equal(12_000m, (await _store.GetInvestmentSettingsAsync()).Exemption);
    }

    [Fact]
    public async Task An_import_creates_its_lines_and_operations_together_and_refuses_an_oversold_file()
    {
        var holding = await SeedAsync();
        var csv = "Date;Type;Produit;ISIN;Quantité;Cours;Devise\n02/02/2026;Achat;Amundi MSCI Europe;LU1681042609;4;250;EUR\n03/02/2026;Vente;World;;2;60;EUR";
        var (_, rows) = BankCsv.Parse(csv);
        var ops = BankCsv.ToOperations(rows.Skip(1).Cast<IReadOnlyList<string>>().ToList(), BankCsv.GuessMapping(rows[0]), false);
        var plan = CsvImportPlanner.Build(ops, await _store.GetHoldingsAsync(), await _store.GetPurchasesAsync(), [], [], "EUR", new Dictionary<string, IReadOnlyList<DailyClose>?>(), Today);

        Assert.Equal(2, await _store.ImportAsync(plan, TaxEnvelope.Standard, _ => "#10B981", computeTob: true, Today));

        var created = (await _store.GetHoldingsAsync()).Single(h => h.Id != holding);
        Assert.Equal((HoldingType.Etf, "LU1681042609"), (created.Type, created.Code));
        // No TOB in the file: 0.12 % of 1000 for an ETF.
        Assert.Equal(1.20m, (await _store.GetPurchasesAsync(created.Id)).Single().Tob);
        Assert.Equal(0.14m, (await _store.GetSalesAsync()).Single().Tob);

        var tooMuch = CsvImportPlanner.Build(Statement2("04/02/2026;Vente;World;;9;60;EUR"), await _store.GetHoldingsAsync(), await _store.GetPurchasesAsync(), [], [], "EUR", new Dictionary<string, IReadOnlyList<DailyClose>?>(), Today);
        var error = await Assert.ThrowsAsync<BudgetException>(() => _store.ImportAsync(tooMuch, TaxEnvelope.Standard, _ => "#10B981", true, Today));
        Assert.Equal(BudgetError.SaleExceedsHoldings, error.Error);
        Assert.Single(await _store.GetSalesAsync());
    }

    private static List<CsvOperation> Statement2(string row)
    {
        var (_, rows) = BankCsv.Parse("Date;Type;Produit;ISIN;Quantité;Cours;Devise\n" + row);
        return BankCsv.ToOperations(rows.Skip(1).Cast<IReadOnlyList<string>>().ToList(), BankCsv.GuessMapping(rows[0]), false);
    }
}
