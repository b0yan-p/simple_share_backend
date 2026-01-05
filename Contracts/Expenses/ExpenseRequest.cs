using System.ComponentModel.DataAnnotations;

namespace simpleShare_backend.Contracts.Expenses;

public class ExpenseRequest
{
    [MaxLength(200)]
    public required string Description { get; set; }
    required public DateTime ExpenseDate { get; set; }
    required public decimal TotalAmount { get; set; }
    public required ICollection<ExpensePaymentRequest> Payments { get; set; } = [];
    public required ICollection<SplitRequest> Splits { get; set; } = [];
}
