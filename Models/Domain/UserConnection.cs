using simpleShare_backend.Models.Base;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Models.Domain;

public class UserConnection : ICreatable
{
    public Guid UserAId { get; set; }
    public Guid UserBId { get; set; }
    public DateTime CreatedAt { get; set; }
    public User UserA { get; set; } = default!;
    public User UserB { get; set; } = default!;
}
