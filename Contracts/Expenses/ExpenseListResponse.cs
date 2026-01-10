using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Contracts.Expenses;

public class ExpenseListResponse
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public decimal Net { get; set; }
    public ICollection<string> PaidByMembers { get; set; } = [];
}
