using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simpleShare_backend.Common;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Contracts.Expenses;
using simpleShare_backend.Services;

namespace simpleShare_backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ExpenseController(IExpenseService service) : ControllerBase
{
    [HttpPost]
    [Route("{groupId}")]
    public async Task<bool> AddExpenseAsync(
        [FromRoute] Guid groupId,
        [FromBody] ExpenseRequest request
    )
    {
        if (!ModelState.IsValid)
            throw new Exception("Invalid input");

        var result = await service.SaveExpenseAsync(groupId, request, User!.GetUserId());
        return result;
    }

    [HttpGet]
    [Route("{groupId}")]
    public Task<PageResponse<ExpenseListResponse>> GetGroupExpensesAsync(
        [FromRoute] Guid groupId,
        [FromQuery] int skip,
        [FromQuery] int take
    )
    {
        return service.GetGroupExpenseListAsync(groupId, User.GetUserId(), skip, take);
    }

    [HttpGet]
    [Route("{groupId}/{expenseId}")]
    public async Task<ExpenseResponse> GetExpenseAsync([FromRoute] Guid groupId, [FromRoute] Guid expenseId)
    {
        return await service.GetExpenseAsync(groupId, expenseId, User.GetUserId());
    }
}

