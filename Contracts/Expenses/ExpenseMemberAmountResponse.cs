namespace simpleShare_backend.Contracts.Expenses;

public class ExpenseMemberAmountResponse
{
    public Guid MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public decimal Amount { get; set; } = 0m;
}
