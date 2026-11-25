using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Contracts.Expenses;

public record ExpensePayer(Guid MemberId, string DisplayName);

public class ExpenseListResponse
{
    public string GroupCurrency { get; set; } = "";
    public decimal OriginalAmount { get; set; }
    public string OriginalCurrency { get; set; } = "";

    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateOnly ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public decimal Net { get; set; }
    public bool IsSettleUp { get; set; }
    public DateTime CreatedAt { get; set; }
    public ICollection<ExpensePayer> Payers { get; set; } = [];
    public ICollection<string> PaidByMembers { get; set; } = [];
}
