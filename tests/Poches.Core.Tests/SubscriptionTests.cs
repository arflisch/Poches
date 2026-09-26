using Poches.Core.Backup;
using Poches.Core.Data;
using Poches.Core.Models;
using Poches.Core.Subscriptions;

namespace Poches.Core.Tests;

public sealed class BillingScheduleTests
{
    private static readonly DateTime Today = new(2026, 9, 26);

    [Theory]
    [InlineData("2026-09-26", "2026-09-26")] // due today
    [InlineData("2026-10-03", "2026-10-03")] // anchor in the future
    [InlineData("2026-09-15", "2026-10-15")] // already paid this month
    [InlineData("2025-01-31", "2026-09-30")] // month-end: September has 30 days
    [InlineData("2024-02-29", "2026-09-29")]
    public void Monthly_next_payment(string anchor, string expected)
    {
        Assert.Equal(DateTime.Parse(expected), BillingSchedule.NextPayment(DateTime.Parse(anchor), BillingPeriod.Monthly, Today));
    }

    [Fact]
    public void A_payment_on_the_31st_returns_to_the_31st_after_short_months()
    {
        var payments = BillingSchedule.Payments(new DateTime(2026, 1, 31), BillingPeriod.Monthly, new DateTime(2026, 1, 1))
            .Take(4)
            .ToList();

        Assert.Equal([new(2026, 1, 31), new(2026, 2, 28), new(2026, 3, 31), new(2026, 4, 30)], payments);
    }

    [Theory]
    [InlineData(BillingPeriod.Weekly, "2026-09-01", "2026-09-29")]
    [InlineData(BillingPeriod.Quarterly, "2026-01-10", "2026-10-10")]
    [InlineData(BillingPeriod.Semiannual, "2026-04-10", "2026-10-10")]
    [InlineData(BillingPeriod.Semiannual, "2025-03-31", "2026-09-30")] // month-end: September has 30 days
    [InlineData(BillingPeriod.Semiannual, "2026-09-26", "2026-09-26")]
    [InlineData(BillingPeriod.Yearly, "2020-03-01", "2027-03-01")]
    [InlineData(BillingPeriod.Yearly, "2024-02-29", "2027-02-28")]
    public void Other_periods(BillingPeriod period, string anchor, string expected)
    {
        Assert.Equal(DateTime.Parse(expected), BillingSchedule.NextPayment(DateTime.Parse(anchor), period, Today));
    }

    [Theory]
    [InlineData(BillingPeriod.Weekly, 10, 43.333, 520)]
    [InlineData(BillingPeriod.Monthly, 13.49, 13.49, 161.88)]
    [InlineData(BillingPeriod.Quarterly, 30, 10, 120)]
    [InlineData(BillingPeriod.Semiannual, 246, 41, 492)]
    [InlineData(BillingPeriod.Yearly, 69.90, 5.825, 69.90)]
    public void Costs_are_normalised(BillingPeriod period, double amount, double monthly, double yearly)
    {
        Assert.Equal((decimal)monthly, decimal.Round(BillingSchedule.MonthlyCost((decimal)amount, period), 3));
        Assert.Equal((decimal)yearly, BillingSchedule.YearlyCost((decimal)amount, period));
    }
}

public sealed class SubscriptionStoreTests : IAsyncLifetime
{
    private static readonly DateTime Today = new(2026, 9, 26);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"poches-subs-{Guid.NewGuid():N}.db3");
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
    public async Task Overview_sorts_by_next_payment_and_totals_active_ones_only()
    {
        await Save("Spotify", 11.12m, BillingPeriod.Monthly, Today.AddDays(9));
        await Save("Netflix", 13.49m, BillingPeriod.Monthly, Today.AddDays(1));
        await Save("Assurance", 180m, BillingPeriod.Yearly, Today.AddDays(200));
        await Save("Disney+", 9.99m, BillingPeriod.Monthly, Today, active: false);

        var overview = await _store.GetSubscriptionOverviewAsync(Today);

        Assert.Equal(["Netflix", "Spotify", "Assurance"], overview.Active.Select(s => s.Subscription.Name));
        Assert.Equal("Disney+", Assert.Single(overview.Inactive).Subscription.Name);
        Assert.Equal(39.61m, overview.MonthlyTotal);
        Assert.Equal(475.32m, overview.YearlyTotal);
        Assert.Equal("Netflix", overview.Next!.Subscription.Name);
        Assert.Equal(Today.AddDays(1), overview.Next.NextPayment);
    }

    [Fact]
    public async Task Invalid_subscriptions_are_refused()
    {
        var noName = await Assert.ThrowsAsync<BudgetException>(() => Save("  ", 5m, BillingPeriod.Monthly, Today));
        var noAmount = await Assert.ThrowsAsync<BudgetException>(() => Save("Netflix", 0m, BillingPeriod.Monthly, Today));

        Assert.Equal(BudgetError.EmptySubscriptionName, noName.Error);
        Assert.Equal(BudgetError.InvalidAmount, noAmount.Error);
    }

    [Fact]
    public async Task Update_and_delete()
    {
        var netflix = await Save("Netflix", 13.49m, BillingPeriod.Monthly, Today);
        netflix.Amount = 15.99m;
        netflix.IsActive = false;
        await _store.SaveSubscriptionAsync(netflix);

        var stored = (await _store.GetSubscriptionAsync(netflix.Id))!;
        Assert.Equal(15.99m, stored.Amount);
        Assert.False(stored.IsActive);

        await _store.DeleteSubscriptionAsync(netflix.Id);
        Assert.Null(await _store.GetSubscriptionAsync(netflix.Id));
    }

    [Fact]
    public async Task Samples_are_only_added_to_an_empty_list()
    {
        await _store.SeedSampleSubscriptionsAsync(
            Today, new SampleSubscriptionTexts("Gym", "Home insurance", "Health insurance", "Car insurance", "Car loan"));
        await _store.SeedSampleSubscriptionsAsync(Today);

        var overview = await _store.GetSubscriptionOverviewAsync(Today);
        Assert.Equal(9, overview.Active.Count);
        Assert.Single(overview.Inactive);
        Assert.Contains(overview.Active, s => s.Subscription.Name == "Gym");
        Assert.Equal(
            [ChargeCategory.Subscription, ChargeCategory.Insurance, ChargeCategory.Loan],
            overview.ByCategory.Select(c => c.Category).Order());
    }

    [Fact]
    public async Task Totals_are_grouped_by_category_most_expensive_first()
    {
        await Save("Netflix", 13.49m, BillingPeriod.Monthly, Today);
        await Save("Spotify", 11.12m, BillingPeriod.Monthly, Today);
        await Save("Mutuelle", 42.50m, BillingPeriod.Monthly, Today, category: ChargeCategory.Insurance);
        await Save("Assurance auto", 246m, BillingPeriod.Semiannual, Today, category: ChargeCategory.Insurance);
        await Save("Loyer", 850m, BillingPeriod.Monthly, Today, category: ChargeCategory.Housing, active: false);

        var byCategory = (await _store.GetSubscriptionOverviewAsync(Today)).ByCategory;

        Assert.Equal(
            [(ChargeCategory.Insurance, 83.50m, 2), (ChargeCategory.Subscription, 24.61m, 2)],
            byCategory.Select(c => (c.Category, c.MonthlyCost, c.Count)));
    }

    [Fact]
    public async Task Unknown_categories_are_refused()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Save("Mystère", 5m, BillingPeriod.Monthly, Today, category: (ChargeCategory)42));
    }

    [Fact]
    public async Task Backups_include_subscriptions()
    {
        await Save("Netflix", 13.49m, BillingPeriod.Monthly, Today.AddDays(1));
        await Save("Disney+", 9.99m, BillingPeriod.Quarterly, Today, active: false);
        await Save("Assurance auto", 246m, BillingPeriod.Semiannual, Today.AddDays(40), category: ChargeCategory.Insurance);
        var backup = await RoundTripAsync(await _store.ExportAsync(Today));

        await _store.DeleteAllAsync();
        await _store.ImportAsync(backup);

        var overview = await _store.GetSubscriptionOverviewAsync(Today);
        Assert.Equal(2, overview.Active.Count);
        var netflix = overview.Active[0];
        Assert.Equal(("Netflix", 1349L, Today.AddDays(1)), (netflix.Subscription.Name, netflix.Subscription.AmountCents, netflix.NextPayment));
        var car = overview.Active[1].Subscription;
        Assert.Equal((BillingPeriod.Semiannual, ChargeCategory.Insurance), (car.Period, car.Category));
        Assert.Equal(BillingPeriod.Quarterly, Assert.Single(overview.Inactive).Subscription.Period);
    }

    [Fact]
    public async Task Charges_from_a_backup_without_categories_are_subscriptions()
    {
        // Version 2 files have no "category" on their subscriptions.
        const string versionTwo = """
            {
              "format": "poches-backup", "version": 2, "exportedAt": "2026-09-25T10:00:00", "currency": "€",
              "pockets": [], "movements": [],
              "subscriptions": [
                { "name": "Netflix", "icon": "🎬", "colorHex": "#F43F5E", "amountCents": 1349, "period": "Monthly",
                  "billingAnchor": "2026-09-27T00:00:00", "isActive": true, "createdAt": "2026-09-01T00:00:00" }
              ]
            }
            """;

        await _store.ImportAsync(BackupSerializer.Read(System.Text.Encoding.UTF8.GetBytes(versionTwo)));

        var netflix = Assert.Single((await _store.GetSubscriptionOverviewAsync(Today)).Active).Subscription;
        Assert.Equal(ChargeCategory.Subscription, netflix.Category);
    }

    [Fact]
    public async Task Restoring_a_backup_made_before_subscriptions_keeps_the_current_ones()
    {
        await Save("Netflix", 13.49m, BillingPeriod.Monthly, Today);
        var oldBackup = (await _store.ExportAsync(Today)) with { Version = 1, Subscriptions = [] };

        await _store.ImportAsync(oldBackup);

        Assert.Single((await _store.GetSubscriptionOverviewAsync(Today)).Active);
    }

    [Fact]
    public async Task Delete_all_removes_subscriptions()
    {
        await Save("Netflix", 13.49m, BillingPeriod.Monthly, Today);

        await _store.DeleteAllAsync();

        var overview = await _store.GetSubscriptionOverviewAsync(Today);
        Assert.Empty(overview.Active);
        Assert.Empty(overview.Inactive);
    }

    private Task<Subscription> Save(
        string name, decimal amount, BillingPeriod period, DateTime anchor, bool active = true,
        ChargeCategory category = ChargeCategory.Subscription) =>
        _store.SaveSubscriptionAsync(new Subscription
        {
            Name = name, Amount = amount, Period = period, BillingAnchor = anchor, IsActive = active, Category = category,
        });

    private static async Task<BackupDocument> RoundTripAsync(BackupDocument document)
    {
        using var stream = new MemoryStream();
        await BackupSerializer.WriteAsync(document, stream);
        stream.Position = 0;
        return await BackupSerializer.ReadAsync(stream);
    }
}
