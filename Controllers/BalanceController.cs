using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Contracts.Balances;
using simpleShare_backend.Services;

namespace simpleShare_backend.Controllers;

/// <summary>
/// User-scoped balances. The group-scoped ones live on <see cref="GroupController"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class BalanceController(IBalanceService service) : ControllerBase
{
    [HttpGet("me")]
    public async Task<OverallBalanceResponse> GetMyBalanceAsync(CancellationToken ct = default)
    {
        var balance = await service.GetMyOverallBalanceAsync(User.GetUserId(), ct);

        return new OverallBalanceResponse
        {
            OwedToMeTotal = balance.OwedToMeTotal,
            IOweTotal = balance.IOweTotal,
            GroupCount = balance.GroupCount,
        };
    }
}
