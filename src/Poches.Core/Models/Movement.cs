using SQLite;

namespace Poches.Core.Models;

public enum MovementKind
{
    Deposit = 0,
    Withdrawal = 1,
    TransferIn = 2,
    TransferOut = 3,
}

[Table("movements")]
public sealed class Movement
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed, NotNull]
    public int PocketId { get; set; }

    /// <summary>Signed amount in cents: positive adds money to the pocket, negative removes it.</summary>
    public long AmountCents { get; set; }

    public MovementKind Kind { get; set; }

    [MaxLength(120)]
    public string? Note { get; set; }

    [Indexed]
    public DateTime Date { get; set; }

    /// <summary>Shared by the two legs of a transfer so they are always deleted together.</summary>
    [Indexed]
    public string? TransferGroup { get; set; }

    public int? CounterpartPocketId { get; set; }

    [Ignore]
    public decimal Amount => Money.FromCents(AmountCents);

    [Ignore]
    public bool IsTransfer => Kind is MovementKind.TransferIn or MovementKind.TransferOut;
}
