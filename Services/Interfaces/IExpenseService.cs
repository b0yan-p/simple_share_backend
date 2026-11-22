using simpleShare_backend.Common;
using simpleShare_backend.Contracts.Expenses;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Services;

public interface IExpenseService
{
    Task<ExpenseResponse> SaveExpenseAsync(Guid groupId, CreateExpenseRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<ExpenseResponse> UpdateExpenseAsync(Guid groupId, UpdateExpenseRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<PageResponse<ExpenseListResponse>> GetGroupExpenseListAsync(
        Guid groupId,
        Guid userId,
        int skip,
        int take
    );

    Task<ExpenseResponse> GetExpenseAsync(Guid groupId, Guid expenseId, Guid userId);
    Task<bool> DeleteExpenseAsync(Guid groupId, Guid expenseId, Guid userId);
    Task<bool> SettleUpDebtAsync(Guid groupId, SettleUpRequest request, Guid userId, CancellationToken cancellationToken = default);
}
