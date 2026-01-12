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
    public async Task<ExpenseResponse> AddExpenseAsync(
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
    public async Task<PageResponse<ExpenseListResponse>> GetGroupExpensesAsync(
        [FromRoute] Guid groupId,
        [FromQuery] int skip,
        [FromQuery] int take
    )
    {
        return await service.GetGroupExpenseListAsync(groupId, User.GetUserId(), skip, take);
    }

    [HttpGet]
    [Route("{groupId}/{expenseId}")]
    public async Task<ExpenseResponse> GetExpenseAsync([FromRoute] Guid groupId, [FromRoute] Guid expenseId)
    {
        return await service.GetExpenseAsync(groupId, expenseId, User.GetUserId());
    }

    [HttpPut]
    [Route("{groupId}")]
    public async Task<ExpenseResponse> UpdateExpenseAsync([FromRoute] Guid groupId, ExpenseRequest request)
    {
        if (!ModelState.IsValid)
            throw new Exception("Invalid input");

        return await service.UpdateExpenseAsync(groupId, request, User.GetUserId());
    }

    [HttpDelete]
    [Route("{groupId}/{expenseId}")]
    public async Task<IActionResult> DeleteExpenseAsync([FromRoute] Guid groupId, [FromRoute] Guid expenseId)
    {
        var deleted = await service.DeleteExpenseAsync(groupId, expenseId, User.GetUserId());
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPost]
    [Route("{groupId}/settleUp")]
    public async Task<IActionResult> SettleUpDebtAsync([FromRoute] Guid groupId, SettleUpRequest request)
    {
        if (!ModelState.IsValid)
            throw new Exception("Invalid request!");

        var result = await service.SettleUpDebtAsync(groupId, request, User.GetUserId());

        if (!result)
            return NotFound();

        return NoContent();
    }
}

