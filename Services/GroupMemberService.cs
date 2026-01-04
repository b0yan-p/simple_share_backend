using simpleShare_backend.Infrastructure;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Enums;
using simpleShare_backend.Services.Specifications.GroupMembers;

namespace simpleShare_backend.Services;

public class GroupMemberService(IRepositoryAsync repo, IMemberService memberService) : IGroupMemberService
{
    public async Task<GroupMember?> AddExistingMemberIntoGroupAsync(Guid memberId, Guid groupId)
    {
        var member = await memberService.GetMemberAsync(memberId)
            ?? throw new Exception("Member does not exist");

        return await CreateGroupMemberDefaultAsync(member, groupId);
    }

    public async Task<GroupMember?> CreateGroupMemberDefaultAsync(Member member, Guid groupId)
    {
        GroupMemberByIdSpec spec = new(groupId, member.Id);

        var gm = await repo.GetSingleDefaultAsync<GroupMember>(spec);

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

            return await repo.CreateWithCompositeId(gm);
        }
        else if (gm is not null && gm.LeftAt is not null)
        {
            gm.LeftAt = null;
            gm.GroupMemberStatus = GroupMemberStatus.Active;
            return repo.UpdateWithCompositeId(gm);
        }
        else
            throw new Exception("Member is already joined at this group");
    }
}
