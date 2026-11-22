using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace simpleShare_backend.Contracts.Expenses;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class ExpenseFinancialInput
{
    public required DateOnly ExpenseDate { get; set; }
    public required decimal OriginalAmount { get; set; }
    public required string OriginalCurrency { get; set; }
    public required ICollection<ExpensePaymentRequest> Payments { get; set; } = [];
    public required ICollection<SplitRequest> Splits { get; set; } = [];
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CreateExpenseRequest : ExpenseFinancialInput
{
    public Guid ClientExpenseId { get; set; }
    [Required, MaxLength(200)] public required string Description { get; set; }
}
