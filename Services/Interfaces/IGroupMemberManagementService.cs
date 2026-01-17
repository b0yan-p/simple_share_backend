using simpleShare_backend.Contracts.Member;

namespace simpleShare_backend.Services;

public interface IGroupMemberManagementService
{
    Task<AddGroupMembersResponse> AddGroupMembersAsync(
        Guid groupId,
        Guid userId,
        AddGroupMembersRequest input
    );
}
