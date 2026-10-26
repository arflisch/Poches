using Poches.Core.Investments;
using Poches.Core.Models;

namespace Poches.Core.Tests;

/// <summary>US capital gains rules: holding period, netting, loss deduction and carryforward, wash sales, accounts.</summary>
public sealed class UsTaxTests
{
    private static readonly DateTime Today = new(2026, 10, 16);

    private static Holding Line(int id, TaxEnvelope envelope = TaxEnvelope.Standard) => new() { Id = id, Name = $"Line {id}", Type = HoldingType.Etf, Envelope = envelope };

    private static Purchase Buy(int id, int holding, DateTime date, decimal qty, decimal price) =>
        new() { Id = id, HoldingId = holding, Date = date, Quantity = qty, UnitPrice = price };

    private static Sale Sell(int id, int holding, DateTime date, decimal qty, decimal price) =>
        new() { Id = id, HoldingId = holding, Date = date, Quantity = qty, UnitPrice = price };

    private static UsTaxReport Report(Holding[] holdings, Purchase[] purchases, Sale[] sales, int year = 2026, Dividend[]? dividends = null)
    {
        var prices = holdings.ToDictionary(h => h.Id, h => new MarketPrice(100, null, Today));
        var performance = InvestmentMath.Compute(holdings, purchases, prices, Today, sales, dividends ?? []);
        return UsTaxReport.Compute(performance, purchases, dividends ?? [], new InvestmentSettings(), year, Today);
    }

    [Fact]
    public void Units_are_long_term_once_held_more_than_a_year()
    {
        Assert.False(UsTax.IsLongTerm(new(2025, 3, 10), new(2026, 3, 10)));
        Assert.True(UsTax.IsLongTerm(new(2025, 3, 10), new(2026, 3, 11)));
    }

    [Fact]
    public void A_net_loss_is_deducted_up_to_the_limit_and_the_rest_carried_with_its_character()
    {
        var (net, carry, deduction) = UsTaxReport.Offset(new UsGains(-5000, 1000), new UsGains(0, 0), 3000);
        Assert.Equal(new UsGains(-4000, 0), net);
        Assert.Equal(3000m, deduction);
        Assert.Equal(new UsGains(-1000, 0), carry);
    }

    [Fact]
    public void Long_term_gains_are_taxed_at_their_rate_and_retirement_accounts_are_left_out()
    {
        Holding[] holdings = [Line(1), Line(2, TaxEnvelope.UsRoth)];
        Purchase[] purchases = [Buy(1, 1, new(2024, 1, 2), 10, 100), Buy(2, 2, new(2026, 1, 2), 10, 100)];
        Sale[] sales = [Sell(1, 1, new(2026, 2, 1), 10, 150), Sell(2, 2, new(2026, 2, 1), 10, 150)];

        var report = Report(holdings, purchases, sales);

        Assert.Equal(new UsGains(0, 500), report.YearGains);
        // 15 % on long-term gains by default.
        Assert.Equal(75m, report.GainsTax);
        Assert.Single(report.Realized);
    }

    [Fact]
    public void A_loss_with_the_same_security_bought_back_within_30_days_is_not_deductible()
    {
        Holding[] holdings = [Line(1)];
        Purchase[] purchases = [Buy(1, 1, new(2026, 1, 5), 10, 100), Buy(2, 1, new(2026, 3, 15), 10, 82)];
        Sale[] sales = [Sell(1, 1, new(2026, 3, 1), 10, 80)];

        var report = Report(holdings, purchases, sales);

        Assert.Equal(-200m, report.Realized.Single().Gain);
        Assert.Equal(200m, report.WashSaleDisallowed);
        Assert.Equal(new UsGains(0, 0), report.YearGains);
    }

    [Fact]
    public void Losses_beyond_the_limit_carry_into_the_next_year()
    {
        Holding[] holdings = [Line(1)];
        Purchase[] purchases = [Buy(1, 1, new(2025, 1, 5), 100, 100), Buy(2, 1, new(2025, 9, 1), 1, 100)];
        Sale[] sales = [Sell(1, 1, new(2025, 6, 1), 100, 50)];

        var report = Report(holdings, purchases, sales);

        // 5,000 lost in 2025: 3,000 deducted that year, 2,000 left for 2026.
        Assert.Equal(new UsGains(-2000, 0), report.CarryIn);
        Assert.Equal(2000m, report.LossDeduction);
    }
}
