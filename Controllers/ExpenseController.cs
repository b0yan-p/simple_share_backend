using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
}

