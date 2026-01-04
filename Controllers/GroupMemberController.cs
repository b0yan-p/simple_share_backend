using Microsoft.AspNetCore.Mvc;
using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Services;

namespace simpleShare_backend.Controllers;

[ApiController]
[Route("api/[controller]/{groupId}")]
public class GroupMemberController(IGroupMemberManagementService service) : ControllerBase
{
    [HttpPost]
    [Route("members/bulk")]
    public async Task<AddGroupMembersResponse> AddGroupMembersAsync(Guid groupId, [FromBody] AddGroupMembersRequest input)
    {
        var result = await service.AddGroupMembersAsync(groupId, input);
        return result;
    }
}
