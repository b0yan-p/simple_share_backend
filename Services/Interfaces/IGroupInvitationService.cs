using simpleShare_backend.Contracts.Invitation;

namespace simpleShare_backend.Services;

public interface IGroupInvitationService
{
    Task<GroupInvitationResponse> GetForGroupAsync(Guid groupId, Guid userId);
    Task<InvitePreviewResponse> GetPreviewAsync(string token);
    Task<JoinGroupResponse> JoinAsync(string token, Guid userId);
}
