using simpleShare_backend.Common;
using simpleShare_backend.Contracts.Group;

namespace simpleShare_backend.Services;

public interface IGroupService
{
    Task<GroupResponse> CreateGroupAsync(CreateGroupRequest request);
    Task<GroupResponse> GetGroupAsync(Guid id);
    Task<PageResponse<GroupResponse>> GetMyGroupsAsync(int skip, int take, string? query = "");
    Task<GroupResponse> DeleteGroupAsync(Guid id);
    Task<GroupResponse> RestoreGroupAsync(Guid id);
    // TODO add update group
}
