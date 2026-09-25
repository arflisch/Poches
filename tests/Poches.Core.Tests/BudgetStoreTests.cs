using Poches.Core.Data;
using Poches.Core.Models;

namespace Poches.Core.Tests;

public sealed class BudgetStoreTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"poches-{Guid.NewGuid():N}.db3");
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
    public async Task Balance_is_the_sum_of_movements_and_total_sums_pockets()
    {
        var holidays = await _store.SavePocketAsync(new Pocket { Name = "Vacances" }, initialAmount: 100m);
        var savings = await _store.SavePocketAsync(new Pocket { Name = "Épargne" });

        await _store.AddMovementAsync(holidays.Id, MovementKind.Deposit, 50.25m, "Prime", DateTime.Now);
        await _store.AddMovementAsync(holidays.Id, MovementKind.Withdrawal, 20m, null, DateTime.Now);
        await _store.AddMovementAsync(savings.Id, MovementKind.Deposit, 1000m, null, DateTime.Now);

        var overview = await _store.GetOverviewAsync(DateTime.Now);

        Assert.Equal(1130.25m, overview.Total);
        Assert.Equal(["Épargne", "Vacances"], overview.Pockets.Select(p => p.Pocket.Name));
        Assert.Equal(130.25m, overview.Pockets.Single(p => p.Pocket.Id == holidays.Id).Balance);
    }

    [Fact]
    public async Task Month_delta_ignores_transfers_and_older_movements()
    {
        var now = new DateTime(2026, 9, 23);
        var a = await _store.SavePocketAsync(new Pocket { Name = "A" });
        var b = await _store.SavePocketAsync(new Pocket { Name = "B" });

        await _store.AddMovementAsync(a.Id, MovementKind.Deposit, 500m, null, now.AddMonths(-1));
        await _store.AddMovementAsync(a.Id, MovementKind.Deposit, 200m, null, now.AddDays(-2));
        await _store.AddMovementAsync(a.Id, MovementKind.Withdrawal, 50m, null, now);
        await _store.TransferAsync(a.Id, b.Id, 100m, null, now);

        var overview = await _store.GetOverviewAsync(now);

        Assert.Equal(150m, overview.MonthDelta);
        Assert.Equal(650m, overview.Total);
    }

    [Fact]
    public async Task Withdrawal_above_balance_is_refused()
    {
        var pocket = await _store.SavePocketAsync(new Pocket { Name = "Vacances" }, 30m);

        var error = await Assert.ThrowsAsync<BudgetException>(
            () => _store.AddMovementAsync(pocket.Id, MovementKind.Withdrawal, 30.01m, null, DateTime.Now));

        Assert.Equal(BudgetError.InsufficientFunds, error.Error);
    }

    [Fact]
    public async Task Transfer_moves_money_and_deleting_one_leg_removes_both()
    {
        var from = await _store.SavePocketAsync(new Pocket { Name = "Épargne" }, 1000m);
        var to = await _store.SavePocketAsync(new Pocket { Name = "Vacances" });

        await _store.TransferAsync(from.Id, to.Id, 250m, "Été", DateTime.Now);

        Assert.Equal(750m, (await _store.GetPocketSummaryAsync(from.Id))!.Balance);
        Assert.Equal(250m, (await _store.GetPocketSummaryAsync(to.Id))!.Balance);

        var incoming = (await _store.GetMovementsAsync(to.Id)).Single();
        Assert.Equal(MovementKind.TransferIn, incoming.Kind);
        Assert.Equal(from.Id, incoming.CounterpartPocketId);

        await _store.DeleteMovementAsync(incoming.Id);

        Assert.Equal(1000m, (await _store.GetPocketSummaryAsync(from.Id))!.Balance);
        Assert.Empty(await _store.GetMovementsAsync(to.Id));
    }

    [Fact]
    public async Task Deleting_a_deposit_that_was_already_spent_is_refused()
    {
        var pocket = await _store.SavePocketAsync(new Pocket { Name = "Vacances" });
        var deposit = await _store.AddMovementAsync(pocket.Id, MovementKind.Deposit, 100m, null, DateTime.Now);
        await _store.AddMovementAsync(pocket.Id, MovementKind.Withdrawal, 80m, null, DateTime.Now);

        var error = await Assert.ThrowsAsync<BudgetException>(() => _store.DeleteMovementAsync(deposit.Id));
        Assert.Equal(BudgetError.BalanceWouldBeNegative, error.Error);
    }

    [Fact]
    public async Task Deleting_a_pocket_removes_its_history_and_keeps_other_pockets_intact()
    {
        var a = await _store.SavePocketAsync(new Pocket { Name = "A" }, 500m);
        var b = await _store.SavePocketAsync(new Pocket { Name = "B" });
        await _store.TransferAsync(a.Id, b.Id, 200m, null, DateTime.Now);

        await _store.DeletePocketAsync(a.Id);

        var overview = await _store.GetOverviewAsync(DateTime.Now);
        var remaining = Assert.Single(overview.Pockets);
        Assert.Equal(200m, remaining.Balance);
        Assert.Null((await _store.GetMovementsAsync(b.Id)).Single().CounterpartPocketId);
    }

    [Fact]
    public async Task Pocket_goal_and_changed_event()
    {
        var changes = 0;
        _store.Changed += (_, _) => changes++;

        var pocket = await _store.SavePocketAsync(new Pocket { Name = "  Voiture  ", Goal = 8000m }, 2000m, "Startbedrag");
        var summary = (await _store.GetPocketSummaryAsync(pocket.Id))!;

        Assert.Equal("Startbedrag", (await _store.GetMovementsAsync(pocket.Id)).Single().Note);

        Assert.Equal("Voiture", summary.Pocket.Name);
        Assert.Equal(0.25, summary.GoalProgress);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Empty_name_is_refused()
    {
        var error = await Assert.ThrowsAsync<BudgetException>(() => _store.SavePocketAsync(new Pocket { Name = "   " }));
        Assert.Equal(BudgetError.EmptyName, error.Error);
    }

    [Fact]
    public async Task Sample_data_is_only_seeded_into_an_empty_database()
    {
        var now = new DateTime(2026, 9, 23, 12, 0, 0);
        var english = SampleTexts.French with { Holidays = "Holidays" };
        await _store.SeedSampleDataAsync(now, english);
        var first = await _store.GetOverviewAsync(now);

        await _store.SeedSampleDataAsync(now);
        var second = await _store.GetOverviewAsync(now);

        Assert.Equal(5, first.Pockets.Count);
        Assert.Contains(first.Pockets, p => p.Pocket.Name == "Holidays");
        Assert.Equal(first.TotalCents, second.TotalCents);
        Assert.All(first.Pockets, p => Assert.True(p.BalanceCents >= 0));
    }

    [Fact]
    public void Balance_history_is_cumulative_per_day()
    {
        var day = new DateTime(2026, 1, 1);
        var history = BudgetStore.BuildBalanceHistory(
        [
            new Movement { AmountCents = 10000, Date = day.AddDays(2) },
            new Movement { AmountCents = 5000, Date = day },
            new Movement { AmountCents = -2000, Date = day.AddDays(2).AddHours(3) },
        ]);

        Assert.Equal([50m, 130m], history.Select(p => p.Balance));
    }
}
