using Poches.Core.Models;

namespace Poches.Core.Subscriptions;

public sealed record SubscriptionSummary(Subscription Subscription, DateTime NextPayment)
{
    public decimal MonthlyCost => BillingSchedule.MonthlyCost(Subscription.Amount, Subscription.Period);

    public decimal YearlyCost => BillingSchedule.YearlyCost(Subscription.Amount, Subscription.Period);
}

/// <param name="Active">Active subscriptions, soonest payment first.</param>
/// <param name="Inactive">Paused or cancelled subscriptions, by name.</param>
public sealed record SubscriptionOverview(IReadOnlyList<SubscriptionSummary> Active, IReadOnlyList<SubscriptionSummary> Inactive)
{
    public decimal MonthlyTotal => Active.Sum(s => s.MonthlyCost);

    public decimal YearlyTotal => Active.Sum(s => s.YearlyCost);

    public SubscriptionSummary? Next => Active.FirstOrDefault();

    public double ShareOf(SubscriptionSummary subscription) =>
        MonthlyTotal > 0 ? (double)(subscription.MonthlyCost / MonthlyTotal) : 0d;

    /// <summary>Monthly cost of the active charges per category, most expensive first.</summary>
    public IReadOnlyList<CategoryTotal> ByCategory =>
        Active.GroupBy(s => s.Subscription.Category)
            .Select(g => new CategoryTotal(g.Key, g.Sum(s => s.MonthlyCost), g.Count()))
            .OrderByDescending(c => c.MonthlyCost)
            .ToList();
}

public sealed record CategoryTotal(ChargeCategory Category, decimal MonthlyCost, int Count);
