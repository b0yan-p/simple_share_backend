using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Infrastructure;
using simpleShare_backend.Models.Application.Activity;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Enums;
using simpleShare_backend.Services.Interfaces;

namespace simpleShare_backend.Services;

public class GroupMemberService(
    IMemberService memberService,
    IGroupService groupService,
    IGroupMemberRepository groupMemberRepo,
    IBalanceService balanceService,
    IActivityWriter activityWriter
) : IGroupMemberService
{
    public async Task<GroupMember?> AddExistingMemberIntoGroupAsync(Guid memberId, Guid groupId)
    {
        var member = await memberService.GetMemberAsync(memberId)
            ?? throw new Exception("Member does not exist");

        return await CreateGroupMemberDefaultAsync(member, groupId);
    }

    public async Task<GroupMember?> CreateGroupMemberDefaultAsync(Member member, Guid groupId)
    {
        var gm = await groupMemberRepo.GetGroupMemberDefaultAsync(groupId, member.Id);

        if (gm is null)
        {
            gm = new()
            {
                MemberId = member.Id,
                GroupId = groupId,
                GroupRole = Models.Enums.GroupRole.Member,
                JoinedAt = DateTime.UtcNow,
                GroupMemberStatus = Models.Enums.GroupMemberStatus.Active
            };

            return await groupMemberRepo.CreateGroupMemberAsync(gm);
        }
        else if (gm is not null && gm.LeftAt is not null)
        {
            gm.LeftAt = null;
            gm.GroupMemberStatus = GroupMemberStatus.Active;
            return groupMemberRepo.UpdateGroupMember(gm);
        }
        else
            throw new Exception("Member is already joined at this group");
    }

    public async Task<ICollection<GroupMemberResponse>> GetAllGroupMembersAsync(Guid groupId)
    {
        _ = await groupService.GetGroupAsync(groupId)
            ?? throw new Exception("Group does not exist!");

        var members = await groupMemberRepo.GetAllAsync(groupId);

        return [.. members.Select(x => x.ToResponse())];
    }

    public async Task<ICollection<GroupMember>> GetAllGroupMembersDefaultAsync(Guid groupId)
    {
        _ = await groupService.GetGroupAsync(groupId)
             ?? throw new Exception("Group does not exist!");

        return await groupMemberRepo.GetAllAsync(groupId);
    }

    public async Task<GroupMember> GetGroupMemberAsync(Guid groupId, Guid memberId)
    {
        var gm = await groupMemberRepo.GetGroupMemberDefaultAsync(groupId, memberId)
            ?? throw new Exception("Member does not exist");

        if (gm.LeftAt is not null || gm.GroupMemberStatus == GroupMemberStatus.Deleted)
            throw new Exception("Member does not exist in this group");

        return gm;
    }

    public async Task<int> GetGroupMemberCountAsync(Guid groupId)
    {
        return await groupMemberRepo.GetGroupMemberCountAsync(groupId);
    }

    public async Task<bool> IsGroupMemberAsync(Guid groupId, Guid memberId)
    {
        return await groupMemberRepo.IsGroupMemberAsync(groupId, memberId);
    }

    public async Task<bool> IsUserGroupMember(Guid groupId, Guid userId)
    {
        return await groupMemberRepo.IsUserGroupMember(groupId, userId);
    }

    public async Task<GroupMember> RemoveGroupMemberAsync(Guid groupId, Guid memberId, Guid userId)
    {
        _ = await groupService.GetGroupAsync(groupId)
           ?? throw new Exception("Group does not exist");

        var memberOwner = await memberService.GetMemberByUserIdAsync(userId)
            ?? throw new Exception("Not authorized to remove members");

        var groupMemberOwner = await GetGroupMemberAsync(groupId, memberOwner.Id);
        if (groupMemberOwner is null || groupMemberOwner.GroupRole != GroupRole.Owner)
            throw new Exception("Not authorized to remove members");

        if (memberId == groupMemberOwner.MemberId)
            throw new Exception("You cannot remove yourself from group");

        var gm = await groupMemberRepo.GetGroupMemberDefaultAsync(groupId, memberId)
            ?? throw new Exception("Member does not exist in this group!");

        if (gm.LeftAt is not null || gm.GroupMemberStatus == GroupMemberStatus.Deleted)
            throw new Exception("Member is already removed from group");

        var (paid, owed, net) = await balanceService.GetMemberNetInGroupAsync(groupId, memberId);

        if (Math.Abs(net) >= 0.01m)
            throw new Exception($"Member cannot leave group. Net is {net:0.00} (Paid {paid:0.00}, Owed {owed:0.00}).");

        gm.LeftAt = DateTime.UtcNow;
        gm.GroupMemberStatus = GroupMemberStatus.Deleted;

        ActivityEvent<MemberActivityData> activity = new(
            Type: ActivityType.MemberRemovedFromGroup,
            EntityType: ActivityEntityType.Member,
            EntityId: gm.MemberId,
            GroupId: groupId,
            ActorMemberId: memberOwner.Id,
            Data: new MemberActivityData(
                TargetMemberId: gm.MemberId,
                TargetMemberName: gm.Member.DisplayName
            )
        );

        activityWriter.Enqueue(activity);

        var result = groupMemberRepo.UpdateGroupMember(gm);
        await groupMemberRepo.SaveChangesAsync();
        return result;
    }
}
