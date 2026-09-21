namespace simpleShare_backend.Models.Application;

/// <summary>
/// The current member's standing across every group they are active in. The totals are sums of
/// the per-group netted debt edges (the same numbers <see cref="MyDebtEdge"/> carries), summed
/// over groups - debts are deliberately not netted across group boundaries.
/// </summary>
public class OverallBalance
{
    /// <summary>Sum of every edge, in every group, where the current member is the creditor.</summary>
    public decimal OwedToMeTotal { get; set; }

    /// <summary>Sum of every edge, in every group, where the current member is the debtor.</summary>
    public decimal IOweTotal { get; set; }

    public decimal Net => OwedToMeTotal - IOweTotal;

    /// <summary>
    /// Number of groups where the current member still has at least one open debt edge. Not the
    /// number of groups with a non-zero net: owing A 10 while B owes you 10 nets to zero but
    /// leaves two open debts, and the totals above count both.
    /// </summary>
    public int GroupCount { get; set; }
}
