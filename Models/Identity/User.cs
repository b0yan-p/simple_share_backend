using Microsoft.AspNetCore.Identity;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Models.Identity;

public class User : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Member? Member { get; set; }
    public ICollection<UserConnection> ConnectionAsA { get; } = [];
    public ICollection<UserConnection> ConnectionsAsB { get; } = [];
}
