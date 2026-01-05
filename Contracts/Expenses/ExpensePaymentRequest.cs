namespace simpleShare_backend.Contracts.Expenses;

public class ExpensePaymentRequest
{
    public required Guid MemberId { get; set; }
    public required decimal Amount { get; set; }
}
