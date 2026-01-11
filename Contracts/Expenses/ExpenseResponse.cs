namespace simpleShare_backend.Contracts.Expenses;

public class ExpenseResponse
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public ICollection<ExpenseMemberAmountResponse> PaidBy { get; set; } = [];
    public ICollection<ExpenseMemberAmountResponse> SplittedBy { get; set; } = [];
}

