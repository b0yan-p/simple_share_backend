using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Services;

namespace simpleShare_backend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class UserConnectionController(
    IUserConnectionService service
) : ControllerBase
{
    [HttpGet]
    public async Task<List<MemberListResponse>> GetMyConnectionsAsync()
    {
        return await service.GetMyConnectionsAsync(User.GetUserId());
    }
}
