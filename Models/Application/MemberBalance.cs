namespace simpleShare_backend.Models.Application;

public class MemberBalance
{
    public Guid MemberId { get; init; }
    public string DisplayName { get; init; } = string.Empty;

    // raw totals decimal(19,4)
    public decimal Paid { get; init; }
    public decimal Owed { get; init; }
    public decimal Net => Paid - Owed;
}
