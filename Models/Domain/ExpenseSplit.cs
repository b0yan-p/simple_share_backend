using simpleShare_backend.Models.Base;

namespace simpleShare_backend.Models.Domain;

public class ExpenseSplit : ICreatable
{
    public Guid ExpenseId { get; set; }
    public Guid OwedByMemberId { get; set; }

    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }

    public Expense Expense { get; set; } = default!;
    public Member OwedByMember { get; set; } = default!;
}
