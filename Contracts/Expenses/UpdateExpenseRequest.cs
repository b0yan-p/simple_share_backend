using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace simpleShare_backend.Contracts.Expenses;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class UpdateExpenseRequest
{
    public required Guid Id { get; set; }
    [MaxLength(200)] public string? Description { get; set; }
    public ExpenseFinancialInput? Financial { get; set; }
}
