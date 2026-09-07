using simpleShare_backend.Contracts.Invitation;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Common.Extensions;

public static class GroupInvitationResponseExtension
{
    public static GroupInvitationResponse ToResponse(this GroupInvitation invitation)
    {
        return new GroupInvitationResponse
        {
            Id = invitation.Id,
            GroupId = invitation.GroupId,
            Token = invitation.Token,
            CreatedAtUtc = invitation.CreatedAtUtc,
            ExpiresAtUtc = invitation.ExpiresAtUtc,
            IsActive = invitation.IsActive,
            UseCount = invitation.UseCount,
        };
    }
}
