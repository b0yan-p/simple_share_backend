using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Models.Application;

public class MyDebtEdge
{
    public Guid OtherMemberId { get; set; }
    public string OtherDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// + Amount => They owe me
    /// - Amount => I owe them
    /// </summary>
    public decimal SignedAmount { get; set; }
    public decimal Amount => Math.Abs(SignedAmount);
    public BalanceSide Side => SignedAmount > 0
        ? BalanceSide.Creditor
        : SignedAmount < 0
            ? BalanceSide.Debtor
            : BalanceSide.Settled;
}
