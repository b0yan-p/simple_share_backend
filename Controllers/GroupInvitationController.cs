using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Contracts.Invitation;
using simpleShare_backend.Services;

namespace simpleShare_backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class GroupInvitationController(IGroupInvitationService service) : ControllerBase
{
    /// <summary>Returns the group's active invite link.</summary>
    [HttpGet]
    [Route("group/{groupId}")]
    public async Task<GroupInvitationResponse> GetForGroupAsync([FromRoute] Guid groupId)
    {
        return await service.GetForGroupAsync(groupId, User.GetUserId());
    }

    /// <summary>
    /// Anonymous on purpose: an invited person usually has no account yet and
    /// has to see which group they are joining before they sign up.
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    [Route("{token}")]
    public async Task<InvitePreviewResponse> GetPreviewAsync([FromRoute] string token)
    {
        return await service.GetPreviewAsync(token);
    }

    [HttpPost]
    [Route("{token}/join")]
    public async Task<JoinGroupResponse> JoinAsync([FromRoute] string token)
    {
        return await service.JoinAsync(token, User.GetUserId());
    }
}
