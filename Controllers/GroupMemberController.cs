using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Services;

namespace simpleShare_backend.Controllers;

[ApiController]
[Route("api/[controller]/{groupId}")]
public class GroupMemberController(
    IGroupMemberManagementService service,
    IGroupMemberService groupMemberService
) : ControllerBase
{
    [HttpPost]
    [Route("members/bulk")]
    public async Task<AddGroupMembersResponse> AddGroupMembersAsync(Guid groupId, [FromBody] AddGroupMembersRequest input)
    {
        var result = await service.AddGroupMembersAsync(groupId, input);
        return result;
    }

    [HttpDelete]
    [Route("members/{memberId}")]
    public async Task<IActionResult> RemoveMemberFromGroupAsync([FromRoute] Guid groupId, [FromRoute] Guid memberId)
    {
        string? id = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (id is not null && Guid.TryParse(id, out Guid userId))
        {
            await groupMemberService.RemoveGroupMemberAsync(groupId, memberId, userId);
            return Ok();
        }
        return Forbid();
    }

    [HttpGet]
    [Route("members")]
    public async Task<ICollection<GroupMemberResponse>> GetAllAsync([FromRoute] Guid groupId)
    {
        return await groupMemberService.GetAllGroupMembersAsync(groupId);
    }
}
