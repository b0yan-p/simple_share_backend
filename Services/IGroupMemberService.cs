using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Services;

public interface IGroupMemberService
{
    Task<GroupMember?> CreateGroupMemberDefaultAsync(Member member, Guid groupId);
    Task<GroupMember?> AddExistingMemberIntoGroupAsync(Guid memberId, Guid groupId);
    Task<GroupMember> RemoveGroupMemberAsync(Guid groupId, Guid memberId);
    Task<ICollection<GroupMemberResponse>> GetAllGroupMembersAsync(Guid groupId);
}
