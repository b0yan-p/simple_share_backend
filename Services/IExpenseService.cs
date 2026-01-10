using simpleShare_backend.Common;
using simpleShare_backend.Contracts.Expenses;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Services;

public interface IExpenseService
{
    Task<bool> SaveExpenseAsync(Guid groupId, ExpenseRequest request, Guid userId);
    Task<ICollection<ExpensePayment>> CreateExpensePaymentsAsync(
        Expense expense,
        IReadOnlySet<Guid> activeMemberIds,
        ExpenseRequest request
    );
    Task<ICollection<ExpenseSplit>> CreateExpenseSplitsAsync(
        Expense expense,
        IReadOnlySet<Guid> activeMemberIds,
        ExpenseRequest request
    );
    Task<Expense> CreateExpenseAsync(Guid groupId, ExpenseRequest request, GroupMember owner);

    Task<PageResponse<ExpenseListResponse>> GetGroupExpenseListAsync(
        Guid groupId,
        Guid userId,
        int skip,
        int take
    );
}
