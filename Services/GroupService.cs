using simpleShare_backend.Contracts.Group;
using simpleShare_backend.Infrastructure;
using simpleShare_backend.Domain;
using simpleShare_backend.Services.Specifications;
using simpleShare_backend.Common;
using simpleShare_backend.Common.Extensions;
using Microsoft.AspNetCore.Authorization;

namespace simpleShare_backend.Services;

public class GroupService(IRepositoryAsync repo) : IGroupService
{
    private readonly IRepositoryAsync _repo = repo;

    public async Task<GroupResponse> CreateGroupAsync(CreateGroupRequest request)
    {
        var newGroup = new Group
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            SimplifyDebts = request.SimplifyDebts ?? false,
            CreatedAt = DateTime.UtcNow
        };

        Group response = await _repo.CreateAsync<Group, Guid>(newGroup);
        await _repo.SaveChangesAsync();

        return response.ToResponse();
    }

    public async Task<GroupResponse> UpdateGroupAsync(CreateGroupRequest request)
    {
        if (request.Id == null)
            throw new Exception("Group ID is required for update.");

        var dbGroup = await _repo.GetByIdAsync<Group, Guid>(request.Id.Value, null);

        dbGroup.ModifiedAt = DateTime.UtcNow;
        dbGroup.Name = request.Name;
        dbGroup.SimplifyDebts = request.SimplifyDebts ?? dbGroup.SimplifyDebts;

        var response = await _repo.UpdateAsync<Group, Guid>(dbGroup);
        await _repo.SaveChangesAsync();

        return response.ToResponse();
    }

    public async Task<GroupResponse> DeleteGroupAsync(Guid id)
    {
        var group = await _repo.DeleteAsync<Group, Guid>(id);
        await _repo.SaveChangesAsync();

        return group.ToResponse();
    }

    public async Task<GroupResponse> RestoreGroupAsync(Guid id)
    {
        var group = await _repo.RestoreAsync<Group, Guid>(id);
        await _repo.SaveChangesAsync();

        return group.ToResponse();
    }

    public async Task<GroupResponse> GetGroupAsync(Guid id)
    {
        var group = await _repo.GetByIdAsync<Group, Guid>(id);

        GroupResponse response = new()
        {
            Id = group.Id,
            Name = group.Name,
            SimplifyDebts = group.SimplifyDebts
        };

        return response;
    }

    public async Task<PageResponse<GroupResponse>> GetMyGroupsAsync(int skip, int take, string? query = "")
    {
        GroupSearchList spec = new(query);
        var result = await _repo.GetAllAsync<Group, Guid>(skip, take, spec);
        var list = result.Data.Select(g => g.ToResponse()).ToList();

        PageResponse<GroupResponse> mappedResult = new(list, result.TotalCount, skip, take);
        return mappedResult;
    }
}
