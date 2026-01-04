using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Services;

public interface IGroupMemberService
{
    Task<GroupMember?> CreateGroupMemberDefaultAsync(Member member, Guid groupId);
    Task<GroupMember?> AddExistingMemberIntoGroupAsync(Guid memberId, Guid groupId);
}
