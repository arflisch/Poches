using SQLite;

namespace Poches.Core.Models;

/// <summary>
/// Money added to a pocket automatically, at a fixed amount and frequency ("200 € into Savings every 1st").
/// Deposits are created when due, including the ones missed while the app was closed.
/// </summary>
[Table("scheduled_deposits")]
public sealed class ScheduledDeposit
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed, NotNull]
    public int PocketId { get; set; }

    public long AmountCents { get; set; }

    public BillingPeriod Period { get; set; } = BillingPeriod.Monthly;

    /// <summary>A deposit date; every other one is derived from it (see <c>BillingSchedule</c>).</summary>
    public DateTime Anchor { get; set; }

    /// <summary>Deposits due up to this date, included, have been added.</summary>
    public DateTime AppliedThrough { get; set; }

    [MaxLength(60)]
    public string? Note { get; set; }

    /// <summary>Paused schedules add nothing, and the deposits missed during the pause are not caught up.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Ignore]
    public decimal Amount
    {
        get => Money.FromCents(AmountCents);
        set => AmountCents = Money.ToCents(value);
    }
}
