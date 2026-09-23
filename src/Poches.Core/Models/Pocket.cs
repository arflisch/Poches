using SQLite;

namespace Poches.Core.Models;

[Table("pockets")]
public sealed class Pocket
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    [NotNull]
    public string Icon { get; set; } = "💰";

    [NotNull]
    public string ColorHex { get; set; } = "#6366F1";

    /// <summary>Optional savings target, in cents.</summary>
    public long? GoalCents { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Ignore]
    public decimal? Goal
    {
        get => GoalCents is { } cents ? Money.FromCents(cents) : null;
        set => GoalCents = value is { } amount ? Money.ToCents(amount) : null;
    }
}
