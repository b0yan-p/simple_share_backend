namespace simpleShare_backend.Contracts.Expenses;

public class ExpenseResponse
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public ExpenseLogResponse CreatedInfo { get; set; } = default!;
    public ExpenseLogResponse? ModifiedInfo { get; set; }
    public ICollection<ExpenseMemberAmountResponse> PaidBy { get; set; } = [];
    public ICollection<ExpenseMemberAmountResponse> SplittedBy { get; set; } = [];
}

public class ExpenseLogResponse
{
    public Guid MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public DateTime ModificationDate { get; set; }
}