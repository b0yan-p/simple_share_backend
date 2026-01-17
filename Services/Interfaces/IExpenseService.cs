using simpleShare_backend.Common;
using simpleShare_backend.Contracts.Expenses;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Services;

public interface IExpenseService
{
    Task<ExpenseResponse> SaveExpenseAsync(Guid groupId, CreateExpenseRequest request, Guid userId);
    Task<ICollection<ExpensePayment>> CreateExpensePaymentsAsync(
        Expense expense,
        IReadOnlySet<Guid> activeMemberIds,
        ICollection<ExpensePaymentRequest> payments
    );
    Task<ICollection<ExpenseSplit>> CreateExpenseSplitsAsync(
        Expense expense,
        IReadOnlySet<Guid> activeMemberIds,
        ICollection<SplitRequest> splits
    );
    Task<Expense> CreateExpenseAsync(Guid groupId, CreateExpenseRequest request, GroupMember owner);
    Task<ExpenseResponse> UpdateExpenseAsync(Guid groupId, UpdateExpenseRequest request, Guid userId);
    Task<PageResponse<ExpenseListResponse>> GetGroupExpenseListAsync(
        Guid groupId,
        Guid userId,
        int skip,
        int take
    );

    Task<ExpenseResponse> GetExpenseAsync(Guid groupId, Guid expenseId, Guid userId);
    Task<bool> DeleteExpenseAsync(Guid groupId, Guid expenseId, Guid userId);
    Task<bool> SettleUpDebtAsync(Guid groupId, SettleUpRequest request, Guid userId);
}
