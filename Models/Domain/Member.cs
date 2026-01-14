using System.ComponentModel.DataAnnotations;
using simpleShare_backend.Models.Base;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Models.Domain;

public class Member : BaseModel<Guid>, IDeletable, ICreatable, IModifiable
{
    [Required]
    [StringLength(80, MinimumLength = 2)]
    public string DisplayName { get; set; } = default!;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    [EmailAddress]
    [MaxLength(256)]
    public string? Email { get; set; }

    //this should be uppercase always
    [MaxLength(256)]
    public string? NormalizedEmail { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public ICollection<GroupMember> GroupMembers { get; } = [];
    public ICollection<Expense> CreatedExpenses { get; } = [];
    public ICollection<ExpensePayment> ExpensePayments { get; } = [];
    public ICollection<ExpenseSplit> ExpenseSplits { get; } = [];
    public ICollection<Activity> ActivitiesAsActor { get; } = [];
}