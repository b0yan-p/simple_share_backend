
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simpleShare_backend.Common;
using simpleShare_backend.Contracts.Group;
using simpleShare_backend.Services;

namespace simpleShare_backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GroupController(IGroupService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateGroupAsync([FromBody] CreateGroupRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var response = await service.CreateGroupAsync(request);
        return Ok(response);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateGroupAsync([FromBody] CreateGroupRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var response = await service.UpdateGroupAsync(request);
        return Ok(response);
    }

    [HttpGet("{groupId}")]
    public async Task<IActionResult> GetGroupByIdAsync([FromRoute] Guid groupId)
    {
        var response = await service.GetGroupAsync(groupId);
        return Ok(response);
    }

    [Authorize]
    [HttpGet]
    public async Task<PageResponse<GroupResponse>> GetMyGroupsAsync(
        [FromQuery] int skip,
        [FromQuery] int take,
        [FromQuery] string? query = ""
    )
    {
        return await service.GetMyGroupsAsync(skip, take, query);
    }

    [HttpDelete("{groupId}")]
    public async Task<IActionResult> DeleteGroupAsync([FromRoute] Guid groupId)
    {
        var response = await service.DeleteGroupAsync(groupId);
        return Ok(response);
    }

    [HttpPost("{groupId}/restore")]
    public async Task<IActionResult> RestoreGroupAsync([FromRoute] Guid groupId)
    {
        var response = await service.RestoreGroupAsync(groupId);
        return Ok(response);
    }
}
