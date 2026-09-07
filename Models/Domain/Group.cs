using simpleShare_backend.Models;
using simpleShare_backend.Models.Base;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Domain;

public class Group : BaseModel<Guid>, IDeletable, ICreatable, IModifiable
{
    public string Name { get; set; } = string.Empty;
    public bool SimplifyDebts { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public ICollection<GroupMember> GroupMembers { get; } = [];
    public ICollection<Expense> Expenses { get; } = [];
    public ICollection<Activity> Activities { get; } = [];
    public ICollection<GroupInvitation> Invitations { get; } = [];
}