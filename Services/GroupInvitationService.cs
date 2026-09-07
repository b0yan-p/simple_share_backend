using Microsoft.EntityFrameworkCore;
using Npgsql;
using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Contracts.Invitation;
using simpleShare_backend.Data;
using simpleShare_backend.Infrastructure;
using simpleShare_backend.Models.Application.Activity;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Enums;
using simpleShare_backend.Services.Interfaces;

namespace simpleShare_backend.Services;

public class GroupInvitationService(
    ApplicationDbContext db,
    IGroupService groupService,
    IGroupMemberService groupMemberService,
    IGroupMemberRepository groupMemberRepo,
    IMemberService memberService,
    IUserService userService,
    IUserConnectionService userConnectionService,
    IActivityWriter activityWriter
) : IGroupInvitationService
{
    /// <summary>
    /// A plain read. A group is given its link when it is created and nothing
    /// deactivates one, so the 404 here is defensive rather than a state the API
    /// is expected to reach.
    /// </summary>
    public async Task<GroupInvitationResponse> GetForGroupAsync(Guid groupId, Guid userId)
    {
        _ =
            await groupService.GetGroupAsync(groupId)
            ?? throw new NotFoundAppException("group_not_found", "Group does not exist!");

        if (!await groupMemberRepo.IsUserGroupMember(groupId, userId))
            throw new ForbiddenAppException("not_authorized", "Not a member of this group");

        var invitation =
            await db.GroupInvitations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.GroupId == groupId && x.IsActive)
            ?? throw new NotFoundAppException(
                "invite_not_found",
                "This group has no active invite link"
            );

        return invitation.ToResponse();
    }

    public async Task<InvitePreviewResponse> GetPreviewAsync(string token)
    {
        var invitation = await GetUsableInvitationAsync(token, track: false);

        var group =
            await groupService.GetGroupAsync(invitation.GroupId)
            ?? throw new NotFoundAppException("group_not_found", "Group does not exist!");

        return new InvitePreviewResponse
        {
            GroupId = group.Id,
            GroupName = group.Name,
            MemberCount = await groupMemberRepo.GetGroupMemberCountAsync(group.Id),
        };
    }

    public async Task<JoinGroupResponse> JoinAsync(string token, Guid userId)
    {
        var invitation = await GetUsableInvitationAsync(token, track: true);
        var groupId = invitation.GroupId;

        var member = await ResolveMemberAsync(userId);

        await using var tx = await db.Database.BeginTransactionAsync();

        try
        {
            await groupMemberService.CreateGroupMemberDefaultAsync(member, groupId);
        }
        catch (ConflictAppException ex) when (ex.Code == "member_already_in_group")
        {
            // A shared link routinely reaches someone already in the group.
            // Nothing to record, and the use is not a new one — but from the
            // caller's point of view they are in, so this is not a failure.
            await tx.RollbackAsync();
            db.ChangeTracker.Clear();

            return new JoinGroupResponse
            {
                GroupId = groupId,
                MemberId = member.Id,
                AlreadyMember = true,
            };
        }

        invitation.UseCount++;

        ActivityEvent<MemberActivityData> activity = new(
            Type: ActivityType.MemberAddedToGroup,
            EntityType: ActivityEntityType.Member,
            EntityId: member.Id,
            GroupId: groupId,
            ActorMemberId: member.Id,
            Data: new MemberActivityData(
                TargetMemberId: member.Id,
                TargetMemberName: member.DisplayName
            )
        );

        activityWriter.Enqueue(activity);

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await ConnectToGroupPeersAsync(groupId);

        return new JoinGroupResponse
        {
            GroupId = groupId,
            MemberId = member.Id,
            AlreadyMember = false,
        };
    }

    /// <summary>
    /// A token that never existed and one that was revoked or expired answer
    /// exactly the same: this runs unauthenticated, and telling the two apart
    /// would confirm to a stranger which tokens are real.
    /// </summary>
    private async Task<GroupInvitation> GetUsableInvitationAsync(string token, bool track)
    {
        var query = db.GroupInvitations.AsQueryable();
        if (!track)
            query = query.AsNoTracking();

        var invitation =
            await query.FirstOrDefaultAsync(x => x.Token == token)
            ?? throw new NotFoundAppException("invite_not_found", "Invite link is not valid");

        var usable =
            invitation.IsActive
            && invitation.RevokedAtUtc is null
            && (invitation.ExpiresAtUtc is null || invitation.ExpiresAtUtc > DateTime.UtcNow);

        if (!usable)
            throw new NotFoundAppException("invite_not_found", "Invite link is not valid");

        return invitation;
    }

    /// <summary>
    /// Registering does not create a Member row, so the first thing a user ever
    /// joins has to materialise one — same path as GroupService.CreateGroupAsync.
    /// </summary>
    private async Task<Member> ResolveMemberAsync(Guid userId)
    {
        var member = await memberService.GetMemberByUserIdAsync(userId);

        if (member?.DeletedAt is not null)
            throw new ConflictAppException("member_deleted", "Member with this id is deleted!");

        if (member is not null)
            return member;

        var user =
            await userService.GetUserAsync(userId)
            ?? throw new NotFoundAppException("user_not_found", "User not found");

        return await memberService.CreateMemberFromUserAsync(user);
    }

    /// <summary>
    /// Same bookkeeping the bulk add does, so joining by link and being added by
    /// the owner leave the same connection graph behind.
    /// </summary>
    private async Task ConnectToGroupPeersAsync(Guid groupId)
    {
        var userIds = await db
            .GroupMembers.AsNoTracking()
            .Where(x =>
                x.GroupId == groupId
                && x.LeftAt == null
                && x.GroupMemberStatus != GroupMemberStatus.Deleted
                && x.Member.UserId != null
            )
            .Select(x => x.Member.UserId!.Value)
            .Distinct()
            .ToListAsync();

        try
        {
            if (await userConnectionService.AddUserConnectionBulk(userIds))
                await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pgEx
                && pgEx.SqlState == PostgresErrorCodes.UniqueViolation
            )
        {
            db.ChangeTracker.Clear();
        }
    }
}
