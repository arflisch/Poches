namespace Poches.Core.Models;

/// <summary>
/// Amounts are persisted as integer cents to avoid floating point drift in SQLite.
/// </summary>
public static class Money
{
    public const decimal MaxAmount = 1_000_000_000m;

    public static long ToCents(decimal amount) =>
        (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    public static decimal FromCents(long cents) => cents / 100m;
}
