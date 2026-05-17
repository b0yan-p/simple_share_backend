using simpleShare_backend.Common;
using simpleShare_backend.Contracts.Balances;
using simpleShare_backend.Contracts.Group;

namespace simpleShare_backend.Services;

public interface IGroupService
{
    Task<GroupResponse> CreateGroupAsync(CreateGroupRequest request, Guid userId);
    Task<GroupResponse> UpdateGroupAsync(CreateGroupRequest request);
    Task<GroupResponse> GetGroupAsync(Guid id);
    Task<PageResponse<GroupResponse>> GetMyGroupsAsync(int skip, int take, Guid userId, string? query = "");
    Task<GroupResponse> DeleteGroupAsync(Guid id);
    Task<GroupResponse> RestoreGroupAsync(Guid id);
    Task<BalanceResponse> GetGroupBalancesAsync(Guid groupId, Guid userId);
    Task<GroupOverviewResponse> GetGroupOverviewAsync(Guid groupId, Guid userId);
}
