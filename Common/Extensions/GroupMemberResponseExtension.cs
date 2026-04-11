using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Common.Extensions;

public static class GroupMemberResponseExtension
{
    public static GroupMemberResponse ToResponse(this GroupMember other)
    {
        return new GroupMemberResponse
        {
            DisplayName = other.Member.DisplayName,
            GroupMemberStatus = other.GroupMemberStatus,
            GroupRole = other.GroupRole,
            MemberId = other.MemberId,
            UserId = other.Member.UserId,
            JoinedAt = other.JoinedAt
        };
    }
}
