using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Services;

public interface IGroupMemberService
{
    Task<GroupMember> GetGroupMemberAsync(Guid groupId, Guid memberId);
    Task<GroupMember?> CreateGroupMemberDefaultAsync(Member member, Guid groupId);
    Task<GroupMember?> AddExistingMemberIntoGroupAsync(Guid memberId, Guid groupId);
    Task<GroupMember> RemoveGroupMemberAsync(Guid groupId, Guid memberId, Guid userId);
    Task<ICollection<GroupMemberResponse>> GetAllGroupMembersAsync(Guid groupId);
    Task<ICollection<GroupMember>> GetAllGroupMembersDefaultAsync(Guid groupId);
    Task<int> GetGroupMemberCountAsync(Guid groupId);
}
