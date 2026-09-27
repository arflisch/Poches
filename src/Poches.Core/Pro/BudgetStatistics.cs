using Poches.Core.Models;

namespace Poches.Core.Pro;

/// <param name="Month">First day of the month.</param>
/// <param name="Deposits">Money added to the pockets (transfers between pockets are not counted).</param>
/// <param name="Withdrawals">Money taken out of the pockets, as a positive amount.</param>
/// <param name="EndBalance">Total of all pockets at the end of the month (today for the current month).</param>
public sealed record MonthStatistics(DateTime Month, decimal Deposits, decimal Withdrawals, decimal EndBalance)
{
    public decimal Net => Deposits - Withdrawals;
}

/// <param name="MonthlyPace">Average monthly change of the pocket over the recent months.</param>
/// <param name="ReachedOn">Month the goal should be reached at this pace; null when it is not getting closer.</param>
public sealed record GoalProjection(Pocket Pocket, decimal Balance, decimal Goal, decimal MonthlyPace, DateTime? ReachedOn)
{
    public bool IsReached => Balance >= Goal;
}

/// <param name="Months">The last months, oldest first, the current one included.</param>
/// <param name="AverageMonthlySavings">Average net savings of the recent full months.</param>
public sealed record BudgetStatistics(
    IReadOnlyList<MonthStatistics> Months,
    decimal AverageMonthlySavings,
    IReadOnlyList<GoalProjection> Goals)
{
    public decimal TotalDeposits => Months.Sum(m => m.Deposits);

    public decimal TotalWithdrawals => Months.Sum(m => m.Withdrawals);

    public bool HasActivity => Months.Any(m => m.Deposits != 0 || m.Withdrawals != 0);
}

public static class StatisticsCalculator
{
    /// <summary>Full months used to measure the saving pace.</summary>
    public const int PaceMonths = 3;

    public static BudgetStatistics Compute(
        IReadOnlyList<Pocket> pockets, IReadOnlyList<Movement> movements, DateTime today, int monthCount = 12)
    {
        var currentMonth = new DateTime(today.Year, today.Month, 1);
        var months = Enumerable.Range(0, monthCount)
            .Select(i => currentMonth.AddMonths(i - monthCount + 1))
            .Select(start =>
            {
                var end = start.AddMonths(1);
                var inMonth = movements.Where(m => m.Date >= start && m.Date < end).ToList();
                return new MonthStatistics(
                    start,
                    Money.FromCents(inMonth.Where(m => m.Kind == MovementKind.Deposit).Sum(m => m.AmountCents)),
                    Money.FromCents(-inMonth.Where(m => m.Kind == MovementKind.Withdrawal).Sum(m => m.AmountCents)),
                    Money.FromCents(movements.Where(m => m.Date < end).Sum(m => m.AmountCents)));
            })
            .ToList();

        var firstActivity = movements.Count > 0 ? movements.Min(m => m.Date) : today;
        var average = Pace(movements, firstActivity, currentMonth, m => m.Kind is MovementKind.Deposit or MovementKind.Withdrawal);

        var goals = pockets
            .Where(p => p.GoalCents is > 0)
            .Select(pocket =>
            {
                var own = movements.Where(m => m.PocketId == pocket.Id).ToList();
                var balance = Money.FromCents(own.Sum(m => m.AmountCents));
                var goal = pocket.Goal!.Value;
                var pace = Pace(own, firstActivity, currentMonth, _ => true);
                DateTime? reachedOn = balance >= goal ? today.Date
                    : pace > 0 ? currentMonth.AddMonths((int)Math.Ceiling((goal - balance) / pace))
                    : null;
                return new GoalProjection(pocket, balance, goal, pace, reachedOn);
            })
            // Goals in progress first, the soonest on top; then the ones not getting closer; reached ones last.
            .OrderBy(g => g.IsReached)
            .ThenBy(g => g.ReachedOn ?? DateTime.MaxValue)
            .ToList();

        return new BudgetStatistics(months, average, goals);
    }

    /// <summary>
    /// Average monthly net change over the last <see cref="PaceMonths"/> full months, ignoring months before the
    /// first recorded movement; the current month alone when there is no full month yet.
    /// </summary>
    private static decimal Pace(IReadOnlyList<Movement> movements, DateTime firstActivity, DateTime currentMonth, Func<Movement, bool> counts)
    {
        var firstMonth = new DateTime(firstActivity.Year, firstActivity.Month, 1);
        var from = new[] { currentMonth.AddMonths(-PaceMonths), firstMonth }.Max();
        var fullMonths = ((currentMonth.Year - from.Year) * 12) + currentMonth.Month - from.Month;
        if (fullMonths <= 0)
            return Money.FromCents(movements.Where(m => m.Date >= currentMonth && counts(m)).Sum(m => m.AmountCents));

        var cents = movements.Where(m => m.Date >= from && m.Date < currentMonth && counts(m)).Sum(m => m.AmountCents);
        return Math.Round(Money.FromCents(cents) / fullMonths, 2, MidpointRounding.AwayFromZero);
    }
}
