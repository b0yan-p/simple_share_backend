using simpleShare_backend.Models.Base;

namespace simpleShare_backend.Models.Domain;

public class ExpensePayment : ICreatable
{
    public Guid ExpenseId { get; set; }
    public Guid PaidByMemberId { get; set; }

    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }

    public Expense Expense { get; set; } = default!;
    public Member PaidByMember { get; set; } = default!;
}
