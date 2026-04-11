using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Contracts.Member;

public class GroupMemberResponse
{
    public Guid MemberId { get; set; }
    public Guid? UserId { get; set; }
    public string DisplayName { get; set; } = default!;
    public GroupRole GroupRole { get; set; } = GroupRole.Member;
    public GroupMemberStatus GroupMemberStatus { get; set; } = GroupMemberStatus.Active;
    public DateTime JoinedAt { get; set; }
}
