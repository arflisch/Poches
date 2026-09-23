namespace Poches.Core.Models;

public sealed record PocketSummary(Pocket Pocket, long BalanceCents)
{
    public decimal Balance => Money.FromCents(BalanceCents);

    /// <summary>Progress towards the goal, clamped to [0, 1]; null when the pocket has no goal.</summary>
    public double? GoalProgress =>
        Pocket.GoalCents is { } goal && goal > 0
            ? Math.Clamp((double)BalanceCents / goal, 0d, 1d)
            : null;
}

public sealed record BudgetOverview(IReadOnlyList<PocketSummary> Pockets, long TotalCents, long MonthDeltaCents)
{
    public decimal Total => Money.FromCents(TotalCents);

    public decimal MonthDelta => Money.FromCents(MonthDeltaCents);

    public double ShareOf(PocketSummary pocket) =>
        TotalCents > 0 ? Math.Max(0d, (double)pocket.BalanceCents / TotalCents) : 0d;
}

public readonly record struct BalancePoint(DateTime Date, decimal Balance);
