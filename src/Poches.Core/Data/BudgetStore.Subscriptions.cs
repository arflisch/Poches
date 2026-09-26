using Poches.Core.Models;
using Poches.Core.Subscriptions;

namespace Poches.Core.Data;

/// <summary>Localised names of the sample subscriptions that are not brand names.</summary>
public sealed record SampleSubscriptionTexts(string Gym, string HomeInsurance)
{
    public static SampleSubscriptionTexts French { get; } = new("Salle de sport", "Assurance habitation");
}

public sealed partial class BudgetStore
{
    public async Task<SubscriptionOverview> GetSubscriptionOverviewAsync(DateTime today)
    {
        var db = await GetConnectionAsync();
        var summaries = (await db.Table<Subscription>().ToListAsync())
            .Select(s => new SubscriptionSummary(s, BillingSchedule.NextPayment(s.BillingAnchor, s.Period, today)))
            .ToList();

        return new SubscriptionOverview(
            summaries.Where(s => s.Subscription.IsActive)
                .OrderBy(s => s.NextPayment)
                .ThenBy(s => s.Subscription.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            summaries.Where(s => !s.Subscription.IsActive)
                .OrderBy(s => s.Subscription.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList());
    }

    public async Task<Subscription?> GetSubscriptionAsync(int subscriptionId)
    {
        var db = await GetConnectionAsync();
        return await db.FindAsync<Subscription>(subscriptionId);
    }

    public async Task<Subscription> SaveSubscriptionAsync(Subscription subscription)
    {
        subscription.Name = subscription.Name.Trim();
        if (subscription.Name.Length == 0)
            throw new BudgetException(BudgetError.EmptySubscriptionName, "A subscription needs a name.");
        EnsureValidAmount(subscription.Amount);
        if (!Enum.IsDefined(subscription.Period))
            throw new ArgumentOutOfRangeException(nameof(subscription), "Unknown billing period.");
        subscription.BillingAnchor = subscription.BillingAnchor.Date;

        var db = await GetConnectionAsync();
        if (subscription.Id == 0)
        {
            subscription.CreatedAt = DateTime.Now;
            await db.InsertAsync(subscription);
        }
        else
        {
            await db.UpdateAsync(subscription);
        }

        OnChanged();
        return subscription;
    }

    public async Task DeleteSubscriptionAsync(int subscriptionId)
    {
        var db = await GetConnectionAsync();
        await db.DeleteAsync<Subscription>(subscriptionId);
        OnChanged();
    }

    /// <summary>Adds a few typical subscriptions when there are none yet, so the tab can be explored.</summary>
    public async Task SeedSampleSubscriptionsAsync(DateTime today, SampleSubscriptionTexts? texts = null)
    {
        var db = await GetConnectionAsync();
        if (await db.Table<Subscription>().CountAsync() > 0)
            return;

        var t = texts ?? SampleSubscriptionTexts.French;
        today = today.Date;
        Subscription Sample(string name, string icon, string color, decimal amount, BillingPeriod period, int dueInDays, bool active = true) =>
            new()
            {
                Name = name,
                Icon = icon,
                ColorHex = color,
                Amount = amount,
                Period = period,
                BillingAnchor = today.AddDays(dueInDays),
                IsActive = active,
                CreatedAt = DateTime.Now,
            };

        await db.InsertAllAsync(new[]
        {
            Sample("Netflix", "🎬", "#F43F5E", 13.49m, BillingPeriod.Monthly, 1),
            Sample(t.Gym, "🏋️", "#F97316", 29.90m, BillingPeriod.Monthly, 4),
            Sample("Spotify", "🎵", "#10B981", 11.12m, BillingPeriod.Monthly, 9),
            Sample("iCloud+", "☁️", "#3B82F6", 2.99m, BillingPeriod.Monthly, 16),
            Sample("Amazon Prime", "📦", "#0891B2", 69.90m, BillingPeriod.Yearly, 120),
            Sample(t.HomeInsurance, "🏠", "#8B5CF6", 180m, BillingPeriod.Yearly, 200),
            Sample("Disney+", "🍿", "#6366F1", 9.99m, BillingPeriod.Monthly, 12, active: false),
        });
        OnChanged();
    }
}
