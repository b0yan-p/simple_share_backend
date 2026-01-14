using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simpleShare_backend.Common;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Contracts.Activity;
using simpleShare_backend.Services.Interfaces;

namespace simpleShare_backend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ActivityController(IActivityService service) : ControllerBase
{
    [HttpGet("me")]
    public async Task<PageResponse<ActivityResponse>> GetMyActivity(
        [FromQuery] int skip,
        [FromQuery] int take,
        CancellationToken ct = default
    )
    {
        return await service.GetMyActivitiesAsync(User.GetUserId(), skip, take, ct);
    }
}
