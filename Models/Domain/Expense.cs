using System.ComponentModel.DataAnnotations;
using simpleShare_backend.Domain;
using simpleShare_backend.Models.Base;

namespace simpleShare_backend.Models.Domain;

public class Expense : BaseModel<Guid>, IDeletable, ICreatable, IModifiable
{
    public Guid GroupId { get; set; }
    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime ExpenseDate { get; set; }
    public Guid CreatedByMemberId { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    // Navigation 
    public Group Group { get; set; } = default!;
    public Member CreatedByMember { get; set; } = default!;
    public ICollection<ExpensePayment> Payments { get; } = [];
    public ICollection<ExpenseSplit> Splits { get; } = [];
}
