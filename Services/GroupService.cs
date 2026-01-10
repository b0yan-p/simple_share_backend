using simpleShare_backend.Contracts.Group;
using simpleShare_backend.Infrastructure;
using simpleShare_backend.Domain;
using simpleShare_backend.Services.Specifications;
using simpleShare_backend.Common;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Contracts.Balances;
using simpleShare_backend.Models.Application;

namespace simpleShare_backend.Services;

public class GroupService(
    IRepositoryAsync repo,
    IGroupMemberRepository gmRepo,
    IMemberService memberService,
    IUserService userService,
    IBalanceService balanceService
) : IGroupService
{
    public async Task<GroupResponse> CreateGroupAsync(CreateGroupRequest request, Guid userId)
    {
        var newGroup = new Group
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            SimplifyDebts = request.SimplifyDebts ?? false,
            CreatedAt = DateTime.UtcNow
        };

        var response = await repo.CreateAsync<Group, Guid>(newGroup);
        var member = await memberService.GetMemberByUserIdAsync(userId);

        if (member?.DeletedAt is not null)
            throw new Exception("Member with this id is deleted!");

        if (member is null)
        {
            var user = await userService.GetUserAsync(userId) ?? throw new Exception("User not found");
            member = await memberService.CreateMemberFromUserAsync(user);
        }

        GroupMember groupMember = new()
        {
            MemberId = member.Id,
            GroupId = newGroup.Id,
            GroupRole = Models.Enums.GroupRole.Owner,
            JoinedAt = DateTime.UtcNow,
            GroupMemberStatus = Models.Enums.GroupMemberStatus.Active
        };

        await repo.CreateWithCompositeId<GroupMember>(groupMember);

        await repo.SaveChangesAsync();

        return response.ToResponse();
    }

    public async Task<GroupResponse> UpdateGroupAsync(CreateGroupRequest request)
    {
        if (request.Id == null)
            throw new Exception("Group ID is required for update.");

        var dbGroup = await repo.GetByIdAsync<Group, Guid>(request.Id.Value, null)
            ?? throw new Exception("Group is not found");

        dbGroup.ModifiedAt = DateTime.UtcNow;
        dbGroup.Name = request.Name;
        dbGroup.SimplifyDebts = request.SimplifyDebts ?? dbGroup.SimplifyDebts;

        var response = await repo.UpdateAsync<Group, Guid>(dbGroup);
        await repo.SaveChangesAsync();

        return response.ToResponse();
    }

    public async Task<GroupResponse> DeleteGroupAsync(Guid id)
    {
        var group = await repo.DeleteAsync<Group, Guid>(id);
        await repo.SaveChangesAsync();

        return group.ToResponse();
    }

    public async Task<GroupResponse> RestoreGroupAsync(Guid id)
    {
        var group = await repo.RestoreAsync<Group, Guid>(id);
        await repo.SaveChangesAsync();

        return group.ToResponse();
    }

    public async Task<GroupResponse> GetGroupAsync(Guid id)
    {
        var group = await repo.GetByIdAsync<Group, Guid>(id)
            ?? throw new Exception("Group not found!");

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
        var result = await repo.GetAllAsync<Group, Guid>(skip, take, spec);
        var list = result.Data.Select(g => g.ToResponse()).ToList();

        PageResponse<GroupResponse> mappedResult = new(list, result.TotalCount, skip, take);
        return mappedResult;
    }

    public async Task<BalanceResponse> GetGroupBalancesAsync(Guid groupId, Guid userId)
    {
        var _ = await GetGroupAsync(groupId)
            ?? throw new Exception("Group does not exist!");

        var result = await balanceService.GetGroupBalancesAsync(groupId, userId);

        return new()
        {
            GroupId = groupId,
            Members = result.Members,
            Debts = result.Debts
        };
    }

    public async Task<GroupOverviewResponse> GetGroupOverviewAsync(Guid groupId, Guid userId)
    {
        var group = await repo.GetByIdAsync<Group, Guid>(groupId);
        var memberCount = await gmRepo.GetGroupMemberCountAsync(groupId);
        var member = await memberService.GetMemberByUserIdAsync(userId)
            ?? throw new Exception("Member not found!");

        GroupOverviewResponse response = new()
        {
            Id = group.Id,
            Name = group.Name,
            MyDebtSummary = new MySummaryResponse(),
            MemberCount = memberCount
        };

        var balances = await balanceService.GetGroupBalancesAsync(groupId, userId);

        var debts = balances.Debts
            .Where(x => x.CreditorMemberId == member.Id || x.DebtorMemberId == member.Id)
            .Select(x => new MyDebtEdge
            {
                SignedAmount = x.CreditorMemberId == member.Id ? x.Amount : -x.Amount,
                OtherMemberId = x.CreditorMemberId == member.Id ? x.DebtorMemberId : x.CreditorMemberId,
                OtherDisplayName = x.CreditorMemberId == member.Id ? x.DebtorDisplayName : x.CreditorDisplayName,
            })
            .ToList();

        response.MyDebtSummary.DebtEdges = debts;
        response.MyDebtSummary.PaidTotal = balances.Debts.Where(b => b.CreditorMemberId == member.Id).Sum(x => x.Amount);
        response.MyDebtSummary.OwedTotal = balances.Debts.Where(b => b.DebtorMemberId == member.Id).Sum(x => x.Amount);

        return response;
    }
}
