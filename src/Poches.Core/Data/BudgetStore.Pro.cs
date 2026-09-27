using Poches.Core.Models;
using Poches.Core.Pro;
using Poches.Core.Subscriptions;

namespace Poches.Core.Data;

public sealed partial class BudgetStore
{
    /// <summary>Guards against a runaway catch-up (a weekly deposit forgotten for years).</summary>
    private const int MaxCatchUp = 520;

    public async Task<IReadOnlyList<ScheduledDeposit>> GetScheduledDepositsAsync(int? pocketId = null)
    {
        var db = await GetConnectionAsync();
        var query = db.Table<ScheduledDeposit>();
        if (pocketId is { } id)
            query = query.Where(s => s.PocketId == id);
        return await query.OrderBy(s => s.Id).ToListAsync();
    }

    /// <summary>
    /// Creates or updates a scheduled deposit. From its first save, and whenever its schedule changes, nothing
    /// before <see cref="ScheduledDeposit.Anchor"/> is added; resuming a paused one does not catch up the pause.
    /// </summary>
    public async Task<ScheduledDeposit> SaveScheduledDepositAsync(ScheduledDeposit schedule, DateTime today)
    {
        EnsureValidAmount(schedule.Amount);
        if (!Enum.IsDefined(schedule.Period))
            throw new ArgumentOutOfRangeException(nameof(schedule), "Unknown period.");
        schedule.Anchor = schedule.Anchor.Date;
        schedule.Note = Normalize(schedule.Note);

        var db = await GetConnectionAsync();
        await EnsurePocketExistsAsync(db, schedule.PocketId);

        var stored = schedule.Id == 0 ? null : await db.FindAsync<ScheduledDeposit>(schedule.Id);
        if (stored is null || stored.Anchor != schedule.Anchor || stored.Period != schedule.Period)
            schedule.AppliedThrough = schedule.Anchor.AddDays(-1);
        else if (!stored.IsActive && schedule.IsActive && schedule.AppliedThrough < today.Date.AddDays(-1))
            schedule.AppliedThrough = today.Date.AddDays(-1);

        if (schedule.Id == 0)
        {
            schedule.CreatedAt = DateTime.Now;
            await db.InsertAsync(schedule);
        }
        else
        {
            await db.UpdateAsync(schedule);
        }
        OnChanged();
        return schedule;
    }

    public async Task DeleteScheduledDepositAsync(int scheduleId)
    {
        var db = await GetConnectionAsync();
        await db.DeleteAsync<ScheduledDeposit>(scheduleId);
        OnChanged();
    }

    /// <summary>Next deposit a schedule will add.</summary>
    public static DateTime NextDeposit(ScheduledDeposit schedule) =>
        BillingSchedule.NextPayment(schedule.Anchor, schedule.Period, schedule.AppliedThrough.AddDays(1));

    /// <summary>
    /// Adds every deposit due up to <paramref name="today"/>, including those missed while the app was closed,
    /// dated on their due day. Safe to call often: each deposit is only added once. Returns how many were added.
    /// </summary>
    public async Task<int> ApplyDueScheduledDepositsAsync(DateTime today, string defaultNote)
    {
        var db = await GetConnectionAsync();
        var schedules = await db.Table<ScheduledDeposit>().Where(s => s.IsActive).ToListAsync();
        var added = 0;

        await db.RunInTransactionAsync(conn =>
        {
            foreach (var schedule in schedules)
            {
                if (conn.Find<Pocket>(schedule.PocketId) is null)
                    continue;
                var due = BillingSchedule.Payments(schedule.Anchor, schedule.Period, schedule.AppliedThrough.AddDays(1))
                    .TakeWhile(d => d <= today.Date)
                    .Take(MaxCatchUp)
                    .ToList();
                if (due.Count == 0)
                    continue;

                foreach (var day in due)
                {
                    conn.Insert(new Movement
                    {
                        PocketId = schedule.PocketId,
                        AmountCents = schedule.AmountCents,
                        Kind = MovementKind.Deposit,
                        Note = schedule.Note ?? defaultNote,
                        Date = day,
                    });
                }
                schedule.AppliedThrough = due[^1];
                conn.Update(schedule);
                added += due.Count;
            }
        });

        if (added > 0)
            OnChanged();
        return added;
    }

    public async Task<IReadOnlyList<SplitRule>> GetSplitRulesAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<SplitRule>().ToListAsync();
    }

    /// <summary>Replaces the split rules; shares of 0 are dropped.</summary>
    /// <exception cref="BudgetException">The shares total more than 100 %.</exception>
    public async Task SaveSplitRulesAsync(IEnumerable<SplitRule> rules)
    {
        var kept = rules.Where(r => r.BasisPoints > 0).ToList();
        if (kept.Any(r => r.BasisPoints > SalarySplit.WholeIncome) || kept.Sum(r => r.BasisPoints) > SalarySplit.WholeIncome)
            throw new BudgetException(BudgetError.SplitOverHundredPercent, "Split rules total more than 100 %.");

        var db = await GetConnectionAsync();
        foreach (var rule in kept)
            await EnsurePocketExistsAsync(db, rule.PocketId);

        await db.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<SplitRule>();
            conn.InsertAll(kept);
        });
        OnChanged();
    }

    /// <summary>Adds an income to the pockets following the split rules, as one deposit per pocket.</summary>
    public async Task<SplitResult> ApplySplitAsync(decimal amount, DateTime date, string? note)
    {
        EnsureValidAmount(amount);
        var rules = await GetSplitRulesAsync();
        if (rules.Count == 0)
            throw new BudgetException(BudgetError.NoSplitRules, "No split rule is defined.");

        var result = SalarySplit.Compute(Money.ToCents(amount), rules);
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            foreach (var share in result.Shares.Where(s => s.Cents > 0))
            {
                conn.Insert(new Movement
                {
                    PocketId = share.PocketId,
                    AmountCents = share.Cents,
                    Kind = MovementKind.Deposit,
                    Note = Normalize(note),
                    Date = date,
                });
            }
        });
        OnChanged();
        return result;
    }

    public async Task<BudgetStatistics> GetStatisticsAsync(DateTime today)
    {
        var db = await GetConnectionAsync();
        var pockets = await db.Table<Pocket>().ToListAsync();
        var movements = await db.Table<Movement>().ToListAsync();
        return StatisticsCalculator.Compute(pockets, movements, today);
    }

    /// <summary>Every movement as a CSV file (see <see cref="MovementCsv"/>).</summary>
    public async Task<byte[]> ExportCsvAsync(CsvTexts texts, System.Globalization.CultureInfo culture)
    {
        var db = await GetConnectionAsync();
        var pockets = await db.Table<Pocket>().ToListAsync();
        var movements = await db.Table<Movement>().ToListAsync();
        return MovementCsv.Write(pockets, movements, texts, culture);
    }
}
