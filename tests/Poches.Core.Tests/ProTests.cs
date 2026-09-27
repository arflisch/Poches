using System.Globalization;
using System.Text;
using Poches.Core.Backup;
using Poches.Core.Data;
using Poches.Core.Models;
using Poches.Core.Pro;

namespace Poches.Core.Tests;

public sealed class SalarySplitTests
{
    private static SplitRule Rule(int pocketId, int percent100) => new() { PocketId = pocketId, BasisPoints = percent100 };

    [Fact]
    public void A_salary_is_split_by_percentages()
    {
        var result = SalarySplit.Compute(240_000, [Rule(1, 5000), Rule(2, 2000), Rule(3, 1500), Rule(4, 1500)]);

        Assert.Equal([120_000L, 48_000, 36_000, 36_000], result.Shares.Select(s => s.Cents));
        Assert.Equal(0, result.UnallocatedCents);
    }

    [Fact]
    public void What_the_rules_leave_aside_is_unallocated()
    {
        var result = SalarySplit.Compute(200_000, [Rule(1, 3000), Rule(2, 1000)]);

        Assert.Equal(80_000, result.Shares.Sum(s => s.Cents));
        Assert.Equal(120_000, result.UnallocatedCents);
    }

    [Theory]
    [InlineData(1001, new[] { 5000, 5000 }, new long[] { 501, 500 }, 0)]
    [InlineData(10_000, new[] { 3333, 3333, 3333 }, new long[] { 3333, 3333, 3333 }, 1)]
    [InlineData(100, new[] { 3334, 3333, 3333 }, new long[] { 34, 33, 33 }, 0)]
    [InlineData(7, new[] { 2500, 2500, 2500, 2500 }, new long[] { 2, 2, 2, 1 }, 0)]
    public void Rounding_never_creates_or_loses_a_cent(long amount, int[] points, long[] expected, long unallocated)
    {
        var result = SalarySplit.Compute(amount, points.Select((p, i) => Rule(i + 1, p)).ToList());

        Assert.Equal(expected, result.Shares.Select(s => s.Cents));
        Assert.Equal(amount, result.Shares.Sum(s => s.Cents) + result.UnallocatedCents);
        Assert.Equal(unallocated, result.UnallocatedCents);
    }

    [Fact]
    public void More_than_the_whole_income_is_refused()
    {
        var error = Assert.Throws<BudgetException>(() => SalarySplit.Compute(1000, [Rule(1, 6000), Rule(2, 5000)]));
        Assert.Equal(BudgetError.SplitOverHundredPercent, error.Error);
    }
}

public sealed class StatisticsTests
{
    private static readonly DateTime Today = new(2026, 9, 26);

    private static Movement Move(int pocketId, decimal amount, MovementKind kind, DateTime date) =>
        new() { PocketId = pocketId, AmountCents = Money.ToCents(amount), Kind = kind, Date = date };

    [Fact]
    public void Months_count_deposits_and_withdrawals_but_not_transfers()
    {
        var pockets = new List<Pocket> { new() { Id = 1, Name = "Épargne" }, new() { Id = 2, Name = "Vacances" } };
        var movements = new List<Movement>
        {
            Move(1, 1000m, MovementKind.Deposit, new(2026, 7, 3)),
            Move(1, -200m, MovementKind.Withdrawal, new(2026, 7, 20)),
            Move(1, -300m, MovementKind.TransferOut, new(2026, 8, 1)),
            Move(2, 300m, MovementKind.TransferIn, new(2026, 8, 1)),
            Move(2, 150m, MovementKind.Deposit, new(2026, 9, 10)),
        };

        var stats = StatisticsCalculator.Compute(pockets, movements, Today);

        Assert.Equal(12, stats.Months.Count);
        Assert.Equal((new DateTime(2025, 10, 1), new DateTime(2026, 9, 1)), (stats.Months[0].Month, stats.Months[^1].Month));
        var july = stats.Months.Single(m => m.Month.Month == 7);
        Assert.Equal((1000m, 200m, 800m, 800m), (july.Deposits, july.Withdrawals, july.Net, july.EndBalance));
        var august = stats.Months.Single(m => m.Month.Month == 8);
        Assert.Equal((0m, 0m, 800m), (august.Deposits, august.Withdrawals, august.EndBalance));
        Assert.Equal(950m, stats.Months[^1].EndBalance);
        Assert.Equal(1150m, stats.TotalDeposits);
    }

    [Fact]
    public void Saving_pace_uses_recent_full_months_since_the_first_movement()
    {
        // Started in July: two full months (July +800, August 0) → 400 a month; September is not full yet.
        var movements = new List<Movement>
        {
            Move(1, 1000m, MovementKind.Deposit, new(2026, 7, 3)),
            Move(1, -200m, MovementKind.Withdrawal, new(2026, 7, 20)),
            Move(1, 5000m, MovementKind.Deposit, new(2026, 9, 2)),
        };

        var stats = StatisticsCalculator.Compute([new Pocket { Id = 1 }], movements, Today);

        Assert.Equal(400m, stats.AverageMonthlySavings);
    }

    [Fact]
    public void Goals_are_projected_at_the_current_pace()
    {
        var pockets = new List<Pocket>
        {
            new() { Id = 1, Name = "Vacances", GoalCents = 250_000 },
            new() { Id = 2, Name = "Voiture", GoalCents = 800_000 },
            new() { Id = 3, Name = "Fonds", GoalCents = 50_000 },
        };
        var movements = new List<Movement>
        {
            // Vacances: 1 000 € then +250 € a month for 3 months → 1 750 €, 750 € left at 250 €/month.
            Move(1, 1000m, MovementKind.Deposit, new(2026, 5, 1)),
            Move(1, 250m, MovementKind.Deposit, new(2026, 6, 1)),
            Move(1, 250m, MovementKind.Deposit, new(2026, 7, 1)),
            Move(1, 250m, MovementKind.Deposit, new(2026, 8, 1)),
            // Voiture: no progress.
            Move(2, 1300m, MovementKind.Deposit, new(2026, 5, 1)),
            // Fonds: already reached.
            Move(3, 600m, MovementKind.Deposit, new(2026, 5, 1)),
        };

        var goals = StatisticsCalculator.Compute(pockets, movements, Today).Goals;

        var holidays = goals.Single(g => g.Pocket.Id == 1);
        Assert.Equal((1750m, 250m, new DateTime(2026, 12, 1)), (holidays.Balance, holidays.MonthlyPace, holidays.ReachedOn));
        Assert.Null(goals.Single(g => g.Pocket.Id == 2).ReachedOn);
        Assert.True(goals.Single(g => g.Pocket.Id == 3).IsReached);
        Assert.Equal([1, 2, 3], goals.Select(g => g.Pocket.Id)); // soonest first, then not progressing, then reached
    }
}

public sealed class MovementCsvTests
{
    [Fact]
    public void Csv_uses_the_culture_separator_and_escapes_cells()
    {
        var pockets = new List<Pocket> { new() { Id = 1, Name = "Épargne" }, new() { Id = 2, Name = "Vacances; été" } };
        var movements = new List<Movement>
        {
            new() { Id = 1, PocketId = 1, AmountCents = 123_456, Kind = MovementKind.Deposit, Date = new(2026, 9, 1), Note = "Salaire \"septembre\"" },
            new() { Id = 2, PocketId = 1, AmountCents = -20_000, Kind = MovementKind.TransferOut, Date = new(2026, 9, 2), CounterpartPocketId = 2 },
        };

        var bytes = MovementCsv.Write(pockets, movements, CsvTexts.French, CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Date;Poche;Type;Montant;Note", lines[0]);
        Assert.Equal("2026-09-01;Épargne;Ajout;1234,56;\"Salaire \"\"septembre\"\"\"", lines[1]);
        Assert.Equal("2026-09-02;Épargne;\"Virement vers Vacances; été\";-200,00;", lines[2]);
    }

    [Fact]
    public void English_csv_uses_commas_and_dots()
    {
        var pockets = new List<Pocket> { new() { Id = 1, Name = "Savings" } };
        var movements = new List<Movement> { new() { Id = 1, PocketId = 1, AmountCents = 1050, Kind = MovementKind.Withdrawal, Date = new(2026, 9, 1) } };
        var texts = new CsvTexts("Date", "Pocket", "Type", "Amount", "Note", "Deposit", "Withdrawal", "To {0}", "From {0}");

        var csv = Encoding.UTF8.GetString(MovementCsv.Write(pockets, movements, texts, CultureInfo.GetCultureInfo("en-GB"))[3..]);

        Assert.Contains("2026-09-01,Savings,Withdrawal,10.50,", csv);
    }
}

public sealed class ProStoreTests : IAsyncLifetime
{
    private static readonly DateTime Today = new(2026, 9, 26);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"poches-pro-{Guid.NewGuid():N}.db3");
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
    public async Task Scheduled_deposits_are_added_once_when_due_and_caught_up_later()
    {
        var savings = await _store.SavePocketAsync(new Pocket { Name = "Épargne" });
        await _store.SaveScheduledDepositAsync(
            new ScheduledDeposit { PocketId = savings.Id, Amount = 200m, Period = BillingPeriod.Monthly, Anchor = Today }, Today);

        Assert.Equal(1, await _store.ApplyDueScheduledDepositsAsync(Today, "Versement programmé"));
        Assert.Equal(0, await _store.ApplyDueScheduledDepositsAsync(Today, "Versement programmé"));

        // The app is not opened for three months.
        Assert.Equal(3, await _store.ApplyDueScheduledDepositsAsync(new DateTime(2026, 12, 30), "Versement programmé"));

        var movements = await _store.GetMovementsAsync(savings.Id);
        Assert.Equal(
            [new DateTime(2026, 12, 26), new(2026, 11, 26), new(2026, 10, 26), new(2026, 9, 26)],
            movements.Select(m => m.Date));
        Assert.All(movements, m => Assert.Equal(("Versement programmé", 20_000L), (m.Note, m.AmountCents)));
        Assert.Equal(new DateTime(2027, 1, 26), BudgetStore.NextDeposit((await _store.GetScheduledDepositsAsync())[0]));
    }

    [Fact]
    public async Task A_future_schedule_adds_nothing_before_its_first_date()
    {
        var savings = await _store.SavePocketAsync(new Pocket { Name = "Épargne" });
        await _store.SaveScheduledDepositAsync(
            new ScheduledDeposit { PocketId = savings.Id, Amount = 50m, Period = BillingPeriod.Weekly, Anchor = Today.AddDays(3), Note = "Hebdo" }, Today);

        Assert.Equal(0, await _store.ApplyDueScheduledDepositsAsync(Today, "x"));
        Assert.Equal(1, await _store.ApplyDueScheduledDepositsAsync(Today.AddDays(3), "x"));
        Assert.Equal("Hebdo", (await _store.GetMovementsAsync(savings.Id)).Single().Note);
    }

    [Fact]
    public async Task A_paused_schedule_does_not_catch_up_its_pause()
    {
        var savings = await _store.SavePocketAsync(new Pocket { Name = "Épargne" });
        var schedule = await _store.SaveScheduledDepositAsync(
            new ScheduledDeposit { PocketId = savings.Id, Amount = 100m, Period = BillingPeriod.Monthly, Anchor = Today }, Today);
        await _store.ApplyDueScheduledDepositsAsync(Today, "x");

        schedule.IsActive = false;
        await _store.SaveScheduledDepositAsync(schedule, Today);
        Assert.Equal(0, await _store.ApplyDueScheduledDepositsAsync(new DateTime(2026, 12, 30), "x"));

        schedule.IsActive = true;
        await _store.SaveScheduledDepositAsync(schedule, new DateTime(2027, 1, 5));
        Assert.Equal(0, await _store.ApplyDueScheduledDepositsAsync(new DateTime(2027, 1, 5), "x"));
        Assert.Equal(1, await _store.ApplyDueScheduledDepositsAsync(new DateTime(2027, 1, 26), "x"));
    }

    [Fact]
    public async Task Deleting_a_pocket_removes_its_schedules_and_split_rule()
    {
        var savings = await _store.SavePocketAsync(new Pocket { Name = "Épargne" });
        var holidays = await _store.SavePocketAsync(new Pocket { Name = "Vacances" });
        await _store.SaveScheduledDepositAsync(
            new ScheduledDeposit { PocketId = savings.Id, Amount = 100m, Anchor = Today }, Today);
        await _store.SaveSplitRulesAsync([new SplitRule { PocketId = savings.Id, BasisPoints = 3000 }, new SplitRule { PocketId = holidays.Id, BasisPoints = 1000 }]);

        await _store.DeletePocketAsync(savings.Id);

        Assert.Empty(await _store.GetScheduledDepositsAsync());
        Assert.Equal(holidays.Id, Assert.Single(await _store.GetSplitRulesAsync()).PocketId);
    }

    [Fact]
    public async Task An_income_is_added_to_the_pockets_following_the_split_rules()
    {
        var savings = await _store.SavePocketAsync(new Pocket { Name = "Épargne" });
        var holidays = await _store.SavePocketAsync(new Pocket { Name = "Vacances" });
        await Assert.ThrowsAsync<BudgetException>(() => _store.ApplySplitAsync(1000m, Today, null));

        await _store.SaveSplitRulesAsync([
            new SplitRule { PocketId = savings.Id, BasisPoints = 2000 },
            new SplitRule { PocketId = holidays.Id, BasisPoints = 1000 },
            new SplitRule { PocketId = 999, BasisPoints = 0 }, // dropped: 0 %
        ]);
        var result = await _store.ApplySplitAsync(2400m, Today, "Salaire");

        Assert.Equal(1680m, result.Unallocated);
        Assert.Equal(480m, (await _store.GetPocketSummaryAsync(savings.Id))!.Balance);
        Assert.Equal(240m, (await _store.GetPocketSummaryAsync(holidays.Id))!.Balance);
        Assert.Equal("Salaire", (await _store.GetMovementsAsync(holidays.Id)).Single().Note);
    }

    [Fact]
    public async Task Split_rules_over_the_whole_income_are_refused()
    {
        var savings = await _store.SavePocketAsync(new Pocket { Name = "Épargne" });
        var holidays = await _store.SavePocketAsync(new Pocket { Name = "Vacances" });

        var error = await Assert.ThrowsAsync<BudgetException>(() => _store.SaveSplitRulesAsync([
            new SplitRule { PocketId = savings.Id, BasisPoints = 7000 },
            new SplitRule { PocketId = holidays.Id, BasisPoints = 4000 },
        ]));
        Assert.Equal(BudgetError.SplitOverHundredPercent, error.Error);
    }

    [Fact]
    public async Task Backups_keep_schedules_and_split_rules_on_the_restored_pockets()
    {
        var savings = await _store.SavePocketAsync(new Pocket { Name = "Épargne" });
        await _store.SaveScheduledDepositAsync(
            new ScheduledDeposit { PocketId = savings.Id, Amount = 200m, Anchor = Today, Note = "Mensuel" }, Today);
        await _store.SaveSplitRulesAsync([new SplitRule { PocketId = savings.Id, BasisPoints = 2500 }]);

        using var stream = new MemoryStream();
        await BackupSerializer.WriteAsync(await _store.ExportAsync(Today), stream);
        stream.Position = 0;
        var backup = await BackupSerializer.ReadAsync(stream);

        await _store.DeleteAllAsync();
        await _store.SavePocketAsync(new Pocket { Name = "Autre" }); // shifts the ids
        await _store.ImportAsync(backup);

        var restored = Assert.Single(await _store.GetPocketSummariesAsync()).Pocket;
        var schedule = Assert.Single(await _store.GetScheduledDepositsAsync());
        Assert.Equal((restored.Id, 20_000L, "Mensuel"), (schedule.PocketId, schedule.AmountCents, schedule.Note));
        Assert.Equal((restored.Id, 2500), Assert.Single(await _store.GetSplitRulesAsync()) is var r ? (r.PocketId, r.BasisPoints) : default);
    }

    [Fact]
    public async Task Restoring_an_older_backup_clears_the_schedules_of_the_replaced_pockets()
    {
        var savings = await _store.SavePocketAsync(new Pocket { Name = "Épargne" });
        await _store.SaveScheduledDepositAsync(new ScheduledDeposit { PocketId = savings.Id, Amount = 1m, Anchor = Today }, Today);
        var older = (await _store.ExportAsync(Today)) with { Version = 3, ScheduledDeposits = [], SplitRules = [] };

        await _store.ImportAsync(older);

        Assert.Empty(await _store.GetScheduledDepositsAsync());
    }
}
