using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Common;
using simpleShare_backend.Common.Currencies;
using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Common.Security;
using simpleShare_backend.Contracts.Balances;
using simpleShare_backend.Contracts.Group;
using simpleShare_backend.Data;
using simpleShare_backend.Domain;
using simpleShare_backend.Infrastructure;
using simpleShare_backend.Models.Application;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Services;

public class GroupService(
    IRepositoryAsync repo,
    IGroupMemberRepository gmRepo,
    IMemberService memberService,
    IUserService userService,
    IBalanceService balanceService,
    ApplicationDbContext db
) : IGroupService
{
    public async Task<GroupResponse> CreateGroupAsync(CreateGroupRequest request, Guid userId)
    {
        var newGroup = new Group
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Currency = CurrencyCatalog.Validate(request.Currency),
            SimplifyDebts = request.SimplifyDebts ?? false,
            CreatedAt = DateTime.UtcNow,
        };

        var response = await repo.CreateAsync<Group, Guid>(newGroup);
        var member = await memberService.GetMemberByUserIdAsync(userId);

        if (member?.DeletedAt is not null)
            throw new ConflictAppException("member_deleted", "Member with this id is deleted!");

        if (member is null)
        {
            var user =
                await userService.GetUserAsync(userId)
                ?? throw new NotFoundAppException("user_not_found", "User not found");
            member = await memberService.CreateMemberFromUserAsync(user);
        }

        GroupMember groupMember = new()
        {
            MemberId = member.Id,
            GroupId = newGroup.Id,
            GroupRole = Models.Enums.GroupRole.Owner,
            JoinedAt = DateTime.UtcNow,
            GroupMemberStatus = Models.Enums.GroupMemberStatus.Active,
        };

        await repo.CreateWithCompositeId<GroupMember>(groupMember);

        // Every group carries a share link from birth, in the same save as the
        // group itself. Minting it here rather than on first read keeps the
        // fetch endpoint a plain read, and makes CreatedByUserId mean what it
        // says. Done inline on purpose: IGroupInvitationService depends on
        // IGroupService, so delegating would close a DI cycle.
        GroupInvitation invitation = new()
        {
            Id = Guid.NewGuid(),
            GroupId = newGroup.Id,
            Token = InviteTokenGenerator.Generate(),
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
        };

        db.GroupInvitations.Add(invitation);

        await repo.SaveChangesAsync();

        var result = response.ToResponse();
        result.CanChangeCurrency = true;
        return result;
    }

    public async Task<GroupResponse> UpdateGroupAsync(UpdateGroupRequest request, Guid userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var group = await db.Groups.FromSqlInterpolated($"SELECT * FROM \"Groups\" WHERE \"Id\" = {request.Id} AND \"DeletedAt\" IS NULL FOR UPDATE")
            .SingleOrDefaultAsync() ?? throw new NotFoundAppException("group_not_found", "Group not found.");
        await db.Entry(group).ReloadAsync();
        var membership = await db.GroupMembers.AsNoTracking().FirstOrDefaultAsync(x => x.GroupId == group.Id && x.Member.UserId == userId && x.LeftAt == null && x.GroupMemberStatus != GroupMemberStatus.Deleted)
            ?? throw new ForbiddenAppException("not_group_member", "Not an active group member.");
        if (request.Currency is not null)
        {
            var currency = CurrencyCatalog.Validate(request.Currency);
            if (currency != group.Currency)
            {
                if (membership.GroupRole != GroupRole.Owner) throw new ForbiddenAppException("group_currency_owner_required", "Only the group owner may change its currency.");
                if (await db.Expenses.AnyAsync(x => x.GroupId == group.Id)) throw new ConflictAppException("group_currency_locked", "Currency is locked after the first expense or settlement.");
                group.Currency = currency;
            }
        }
        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name)) throw new ValidationAppException("invalid_request", "Name is required.");
            group.Name = request.Name.Trim();
        }
        group.SimplifyDebts = request.SimplifyDebts ?? group.SimplifyDebts;
        group.ModifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await GetGroupAsync(group.Id, userId);
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

    public async Task<GroupResponse> GetGroupAsync(Guid id, Guid? userId = null)
    {
        var group =
            await repo.GetByIdAsync<Group, Guid>(id)
            ?? throw new NotFoundAppException("group_not_found", "Group not found!");

        GroupResponse response = new()
        {
            Id = group.Id,
            Name = group.Name,
            Currency = group.Currency,
            SimplifyDebts = group.SimplifyDebts,
        };

        response.CurrencyLocked = await db.Expenses.AnyAsync(x => x.GroupId == id);
        response.CanChangeCurrency = !response.CurrencyLocked && userId != null && await db.GroupMembers.AnyAsync(x => x.GroupId == id && x.Member.UserId == userId && x.LeftAt == null && x.GroupMemberStatus != GroupMemberStatus.Deleted && x.GroupRole == GroupRole.Owner);
        return response;
    }

    public async Task<PageResponse<GroupResponse>> GetMyGroupsAsync(
        int skip,
        int take,
        Guid userId,
        string? query = ""
    )
    {
        var myMemberId =
            await db
                .Members.AsNoTracking()
                .Where(m => m.UserId == userId && m.DeletedAt == null)
                .Select(m => (Guid?)m.Id)
                .FirstOrDefaultAsync()
            ?? throw new NotFoundAppException("member_not_found", "Member not found!");

        var groupsQuery = db
            .Groups.AsNoTracking()
            .Where(g =>
                g.DeletedAt == null
                && g.GroupMembers.Any(gm =>
                    gm.MemberId == myMemberId
                    && gm.LeftAt == null
                    && gm.GroupMemberStatus != GroupMemberStatus.Deleted
                )
            );

        if (!string.IsNullOrWhiteSpace(query))
            groupsQuery = groupsQuery.Where(g => g.Name.Contains(query));

        var orderedGroups = groupsQuery
            .OrderByDescending(g => g.ModifiedAt)
            .ThenByDescending(g => g.CreatedAt);

        var totalCount = await groupsQuery.CountAsync();
        var pagedGroupsQuery = take == -1 ? orderedGroups : orderedGroups.Skip(skip).Take(take);

        var pagedGroups = await pagedGroupsQuery
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.Currency,
                CurrencyLocked = g.Expenses.Any(),
                IsOwner = g.GroupMembers.Any(gm => gm.MemberId == myMemberId && gm.GroupRole == GroupRole.Owner),
                g.SimplifyDebts,
                g.CreatedAt,
                g.DeletedAt,
            })
            .ToListAsync();

        var groupIds = pagedGroups.Select(g => g.Id).ToList();

        var balancesByGroup = await balanceService.GetGroupsBalancesAsync(groupIds);

        var list = pagedGroups
            .Select(g =>
            {
                var net = balancesByGroup.GetValueOrDefault(g.Id)?.Members
                    .SingleOrDefault(x => x.MemberId == myMemberId)?.Net ?? 0m;

                return new GroupResponse
                {
                    Id = g.Id,
                    Name = g.Name,
                    Currency = g.Currency,
                    CurrencyLocked = g.CurrencyLocked,
                    CanChangeCurrency = !g.CurrencyLocked && g.IsOwner,
                    SimplifyDebts = g.SimplifyDebts,
                    CreatedAt = g.CreatedAt,
                    DeletedAt = g.DeletedAt,
                    NetBalance = net,
                };
            })
            .ToList();

        PageResponse<GroupResponse> mappedResult = new(list, totalCount, skip, take);
        return mappedResult;
    }

    public async Task<List<GroupResponse>> GetRecentGroupsAsync(Guid userId, int take = 3)
    {
        var myMemberId =
            await db
                .Members.AsNoTracking()
                .Where(m => m.UserId == userId && m.DeletedAt == null)
                .Select(m => (Guid?)m.Id)
                .FirstOrDefaultAsync()
            ?? throw new NotFoundAppException("member_not_found", "Member not found!");

        var groupsQuery = db
            .Groups.AsNoTracking()
            .Where(g =>
                g.DeletedAt == null
                && g.GroupMembers.Any(gm =>
                    gm.MemberId == myMemberId
                    && gm.LeftAt == null
                    && gm.GroupMemberStatus != GroupMemberStatus.Deleted
                )
            );

        var pagedGroups = await groupsQuery
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.Currency,
                CurrencyLocked = g.Expenses.Any(),
                IsOwner = g.GroupMembers.Any(gm => gm.MemberId == myMemberId && gm.GroupRole == GroupRole.Owner),
                g.SimplifyDebts,
                g.CreatedAt,
                g.ModifiedAt,
                g.DeletedAt,
                LastChange = g
                    .Activities.Select(a => (DateTime?)a.CreatedAt)
                    .OrderByDescending(x => x)
                    .FirstOrDefault(),
            })
            .OrderByDescending(g => g.LastChange ?? g.ModifiedAt ?? g.CreatedAt)
            .ThenByDescending(g => g.CreatedAt)
            .Take(take)
            .ToListAsync();

        var groupIds = pagedGroups.Select(g => g.Id).ToList();

        var balancesByGroup = await balanceService.GetGroupsBalancesAsync(groupIds);

        return pagedGroups
            .Select(g =>
            {
                var net = balancesByGroup.GetValueOrDefault(g.Id)?.Members
                    .SingleOrDefault(x => x.MemberId == myMemberId)?.Net ?? 0m;

                return new GroupResponse
                {
                    Id = g.Id,
                    Name = g.Name,
                    Currency = g.Currency,
                    CurrencyLocked = g.CurrencyLocked,
                    CanChangeCurrency = !g.CurrencyLocked && g.IsOwner,
                    SimplifyDebts = g.SimplifyDebts,
                    CreatedAt = g.CreatedAt,
                    DeletedAt = g.DeletedAt,
                    NetBalance = net,
                };
            })
            .ToList();
    }

    public async Task<BalanceResponse> GetGroupBalancesAsync(Guid groupId, Guid userId)
    {
        var _ =
            await GetGroupAsync(groupId)
            ?? throw new NotFoundAppException("group_not_found", "Group does not exist!");

        var member =
            await memberService.GetMemberByUserIdAsync(userId)
            ?? throw new NotFoundAppException("member_not_found", "Member not found!");

        var result = await balanceService.GetGroupBalancesAsync(groupId, userId);

        return new()
        {
            GroupId = groupId,
            Currency = _.Currency,
            CurrentMemberId = member.Id,
            Members = result.Members,
            Debts = result.Debts,
        };
    }

    public async Task<GroupOverviewResponse> GetGroupOverviewAsync(Guid groupId, Guid userId)
    {
        var group =
            await repo.GetByIdAsync<Group, Guid>(groupId)
            ?? throw new NotFoundAppException("group_not_found", "Group not found!");
        var memberCount = await gmRepo.GetGroupMemberCountAsync(groupId);
        var member =
            await memberService.GetMemberByUserIdAsync(userId)
            ?? throw new NotFoundAppException("member_not_found", "Member not found!");

        GroupOverviewResponse response = new()
        {
            Id = group.Id,
            Name = group.Name,
            Currency = group.Currency,
            MyDebtSummary = new MySummaryResponse(),
            MemberCount = memberCount,
            CurrentMemberId = member.Id,
        };

        var balances = await balanceService.GetGroupBalancesAsync(groupId, userId);

        var debts = balances
            .Debts.Where(x => x.CreditorMemberId == member.Id || x.DebtorMemberId == member.Id)
            .Select(x => new MyDebtEdge
            {
                SignedAmount = x.CreditorMemberId == member.Id ? x.Amount : -x.Amount,
                OtherMemberId =
                    x.CreditorMemberId == member.Id ? x.DebtorMemberId : x.CreditorMemberId,
                OtherDisplayName =
                    x.CreditorMemberId == member.Id ? x.DebtorDisplayName : x.CreditorDisplayName,
            })
            .ToList();

        response.MyDebtSummary.DebtEdges = debts;
        response.MyDebtSummary.PaidTotal = balances
            .Debts.Where(b => b.CreditorMemberId == member.Id)
            .Sum(x => x.Amount);
        response.MyDebtSummary.OwedTotal = balances
            .Debts.Where(b => b.DebtorMemberId == member.Id)
            .Sum(x => x.Amount);

        return response;
    }
}
