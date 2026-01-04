using Ardalis.Specification;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Services.Specifications.GroupMembers;

public class GroupMemberByIdSpec : Specification<GroupMember>
{
    public GroupMemberByIdSpec(Guid? groupId, Guid? memberId)
    {
        if (groupId is null || memberId is null)
            return;

        _ = Query.Where(x => x.GroupId == groupId && x.MemberId == memberId);
    }
}