using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Infrastructure;

public interface IGroupMemberRepository
{
    Task<ICollection<GroupMember>> GetAllAsync(Guid groupId);
    Task<GroupMember?> GetGroupMemberDefaultAsync(Guid groupId, Guid memberId);
    Task<GroupMember> CreateGroupMemberAsync(GroupMember item);
    GroupMember UpdateGroupMember(GroupMember item);
    Task<int> SaveChangesAsync();
    Task<int> GetGroupMemberCountAsync(Guid groupId);
}
