using System.ComponentModel.DataAnnotations;

namespace simpleShare_backend.Contracts.Expenses;

public class UpdateExpenseRequest
{
    public required Guid Id { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }
    public DateTime? ExpenseDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public bool? IsSettleUp { get; set; }
    public ICollection<ExpensePaymentRequest>? Payments { get; set; }
    public ICollection<SplitRequest>? Splits { get; set; }
}
