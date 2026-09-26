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
}
