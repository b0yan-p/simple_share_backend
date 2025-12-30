using simpleShare_backend.Domain;
using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Models.Domain;

public class GroupMember
{
    public Guid GroupId { get; set; }
    public Guid MemberId { get; set; }

    public GroupRole GroupRole { get; set; } = GroupRole.Member;
    public GroupMemberStatus GroupMemberStatus { get; set; } = GroupMemberStatus.Active;
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }

    public Group Group { get; set; } = null!;
    public Member Member { get; set; } = null!;
}
