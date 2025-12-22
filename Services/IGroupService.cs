using simpleShare_backend.Common;
using simpleShare_backend.Contracts.Group;
using simpleShare_backend.Domain;

namespace simpleShare_backend.Services;

public interface IGroupService
{
    Task<GroupResponse> CreateGroupAsync(CreateGroupRequest request);
    Task<GroupResponse> GetGroupAsync(Guid id);
    Task<PageResponse<GroupResponse>> GetMyGroupsAsync(int skip, int take, string? query = "");
    // TODO add update group
    // TODO add delete group
    // TODO add restore group
}
