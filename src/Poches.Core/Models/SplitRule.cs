using SQLite;

namespace Poches.Core.Models;

/// <summary>Share of an income that goes to a pocket when it is split (see <c>SalarySplit</c>).</summary>
[Table("split_rules")]
public sealed class SplitRule
{
    [PrimaryKey]
    public int PocketId { get; set; }

    /// <summary>Share in basis points: 1 % is 100, the whole income 10 000.</summary>
    public int BasisPoints { get; set; }
}
