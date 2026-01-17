using simpleShare_backend.Common;
using simpleShare_backend.Contracts.Activity;
using simpleShare_backend.Models.Application.Activity;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Services.Interfaces;

public interface IActivityService
{
    Task<PageResponse<ActivityResponse>> GetMyActivitiesAsync(
        Guid userId,
        int skip,
        int take,
        CancellationToken ct = default
    );

    ActivityEvent<ExpenseUpdatedActivityData> AddExpenseUpdatedActivity(
        Expense oldExpense,
        Expense newExpense,
        Guid ActorMemberId
    );
}
