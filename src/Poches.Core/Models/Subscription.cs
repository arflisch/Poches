using SQLite;

namespace Poches.Core.Models;

public enum BillingPeriod
{
    Weekly = 0,
    Monthly = 1,
    Quarterly = 2,
    Yearly = 3,
}

[Table("subscriptions")]
public sealed class Subscription
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    [NotNull]
    public string Icon { get; set; } = "📺";

    [NotNull]
    public string ColorHex { get; set; } = "#6366F1";

    /// <summary>Price charged every <see cref="Period"/>, in cents.</summary>
    public long AmountCents { get; set; }

    public BillingPeriod Period { get; set; } = BillingPeriod.Monthly;

    /// <summary>A known payment date; every other payment is derived from it (see <c>BillingSchedule</c>).</summary>
    public DateTime BillingAnchor { get; set; }

    /// <summary>Cancelled or paused subscriptions are kept for reference but excluded from totals and reminders.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Ignore]
    public decimal Amount
    {
        get => Money.FromCents(AmountCents);
        set => AmountCents = Money.ToCents(value);
    }
}
