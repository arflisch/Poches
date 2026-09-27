using Poches.Core.Models;

namespace Poches.Core.Pro;

/// <summary>Share of an income given to a pocket.</summary>
public readonly record struct SplitShare(int PocketId, long Cents)
{
    public decimal Amount => Money.FromCents(Cents);
}

/// <summary>How an income is divided between pockets, following the split rules.</summary>
/// <param name="Unallocated">What the rules leave aside when they total less than 100 %.</param>
public sealed record SplitResult(IReadOnlyList<SplitShare> Shares, long UnallocatedCents)
{
    public decimal Unallocated => Money.FromCents(UnallocatedCents);
}

public static class SalarySplit
{
    public const int WholeIncome = 10_000;

    /// <summary>
    /// Splits <paramref name="amountCents"/> by the rules' shares. Rounding never creates or loses a cent: the
    /// allocated total is the income times the total share, rounded once, and the cents left by rounding each
    /// share down go to the shares that lost the most.
    /// </summary>
    public static SplitResult Compute(long amountCents, IReadOnlyList<SplitRule> rules)
    {
        var active = rules.Where(r => r.BasisPoints > 0).ToList();
        var totalPoints = active.Sum(r => r.BasisPoints);
        if (totalPoints > WholeIncome)
            throw new BudgetException(BudgetError.SplitOverHundredPercent, $"Split rules total {totalPoints} basis points.");

        var target = (long)Math.Round(amountCents * (decimal)totalPoints / WholeIncome, MidpointRounding.AwayFromZero);
        var exact = active.Select(r => (r.PocketId, Value: amountCents * (decimal)r.BasisPoints / WholeIncome)).ToList();
        var cents = exact.ToDictionary(e => e.PocketId, e => (long)Math.Floor(e.Value));

        var missing = target - cents.Values.Sum();
        foreach (var (pocketId, _) in exact.OrderByDescending(e => e.Value - Math.Floor(e.Value)).ThenBy(e => e.PocketId))
        {
            if (missing <= 0)
                break;
            cents[pocketId]++;
            missing--;
        }

        var shares = active.Select(r => new SplitShare(r.PocketId, cents[r.PocketId])).ToList();
        return new SplitResult(shares, amountCents - target);
    }
}
