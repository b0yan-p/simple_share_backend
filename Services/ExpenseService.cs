using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Common;
using simpleShare_backend.Contracts.Expenses;
using simpleShare_backend.Data;
using simpleShare_backend.Infrastructure;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Services;

public class ExpenseService(
    ApplicationDbContext db,
    IRepositoryAsync repo,
    IGroupService groupService,
    IMemberService memberService,
    IGroupMemberService groupMemberService
) : IExpenseService
{
    public async Task<Expense> CreateExpenseAsync(Guid groupId, ExpenseRequest request, GroupMember owner)
    {
        if (string.IsNullOrEmpty(request.Description))
            throw new Exception("Description is required");

        if (request.ExpenseDate == default)
            throw new Exception("Expense date is required");

        if (request.TotalAmount <= 0)
            throw new Exception("Total amount must be greater than 0");

        _ = await groupService.GetGroupAsync(groupId)
            ?? throw new Exception("Group does not exist");

        if (owner.LeftAt is not null || owner.GroupMemberStatus == GroupMemberStatus.Deleted)
            throw new Exception("Member is not in the group!");

        Expense expense = new()
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Description = request.Description.Trim(),
            TotalAmount = request.TotalAmount,
            ExpenseDate = request.ExpenseDate,
            CreatedAt = DateTime.UtcNow,
            CreatedByMemberId = owner.MemberId
        };

        return await repo.CreateAsync<Expense, Guid>(expense);
    }

    public async Task<ICollection<ExpensePayment>> CreateExpensePaymentsAsync(
        Expense expense,
        IReadOnlySet<Guid> activeMemberIds,
        ExpenseRequest request
    )
    {
        var payments = request.Payments;

        if (payments is null || payments.Count == 0)
            throw new Exception("Payments are required!");

        if (payments.Any(p => p.Amount <= 0))
            throw new Exception("Payment amount must be greater than ");

        var paymentMemberIds = payments
            .Select(x => x.MemberId)
            .ToList();

        if (paymentMemberIds.Distinct().Count() != payments.Count)
            throw new Exception("Payment list mys be unique");

        if (paymentMemberIds.Any(id => !activeMemberIds.Contains(id)))
            throw new Exception("Payment list contains deleted group members");

        decimal sum = payments.Sum(x => x.Amount);
        if (Math.Abs(sum - expense.TotalAmount) > 0.0001m)
            throw new Exception("Payment amounts are not splitted correctly!");

        List<ExpensePayment> paymentToCreate = [];

        var now = DateTime.UtcNow;
        foreach (var payment in payments)
        {
            paymentToCreate.Add(new()
            {
                Amount = payment.Amount,
                PaidByMemberId = payment.MemberId,
                ExpenseId = expense.Id,
                CreatedAt = now,
            });
        }

        return await repo.AddRangeAsync(paymentToCreate);
    }

    public async Task<ICollection<ExpenseSplit>> CreateExpenseSplitsAsync(
        Expense expense,
        IReadOnlySet<Guid> activeMemberIds,
        ExpenseRequest request
    )
    {
        var splits = request.Splits;

        if (splits is null || splits.Count == 0)
            throw new Exception("Splits are required!");

        if (splits.Any(p => p.Amount <= 0))
            throw new Exception("Split amount must be greater than 0");

        var splitsMemberIds = splits
            .Select(x => x.MemberId)
            .ToList();

        if (splitsMemberIds.Distinct().Count() != splits.Count)
            throw new Exception("Splits list mys be unique");

        if (splitsMemberIds.Any(id => !activeMemberIds.Contains(id)))
            throw new Exception("Split list contains deleted group members");

        decimal sum = splits.Sum(x => x.Amount);
        if (Math.Abs(sum - expense.TotalAmount) > 0.0001m)
            throw new Exception("Split amounts are not splitted correctly!");

        List<ExpenseSplit> splitsToCreate = [];

        var now = DateTime.UtcNow;
        foreach (var split in splits)
        {
            splitsToCreate.Add(new()
            {
                Amount = split.Amount,
                OwedByMemberId = split.MemberId,
                ExpenseId = expense.Id,
                CreatedAt = now,
            });
        }

        return await repo.AddRangeAsync(splitsToCreate);
    }

    public async Task<PageResponse<ExpenseListResponse>> GetGroupExpenseListAsync(
         Guid groupId,
         Guid userId,
         int skip,
         int take
    )
    {
        var meMember = await memberService.GetMemberByUserIdAsync(userId)
            ?? throw new Exception("Member not found!");

        _ = await groupMemberService.GetGroupMemberAsync(groupId, meMember.Id)
            ?? throw new Exception("Not a group member!");

        var expenses = await db.Expenses
            .AsNoTracking()
            .Where(e => e.DeletedAt == null && e.GroupId == groupId)
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.Id)
            .Skip(skip)
            .Take(take)
            .Select(e => new
            {
                e.Id,
                e.Description,
                e.ExpenseDate,
                e.TotalAmount,

                PaidByMembers = e.Payments.Select(p => p.PaidByMember.DisplayName),
                PaidSum = e.Payments
                    .Where(p => p.PaidByMemberId == meMember.Id)
                    .Sum(p => (decimal?)p.Amount) ?? 0m,
                SplitSum = e.Splits
                    .Where(s => s.OwedByMemberId == meMember.Id)
                    .Sum(s => (decimal?)s.Amount) ?? 0m,
            })
            .AsSplitQuery()
            .ToListAsync();

        var result = expenses.Select(e => new ExpenseListResponse()
        {
            Id = e.Id,
            Description = e.Description,
            ExpenseDate = e.ExpenseDate,
            Amount = Math.Round(e.TotalAmount, 2, MidpointRounding.AwayFromZero),
            PaidByMembers = [.. e.PaidByMembers],
            Net = Math.Round(e.PaidSum - e.SplitSum, 2, MidpointRounding.AwayFromZero)
        })
        .ToList();

        var count = await db.Expenses.Where(e => e.DeletedAt == null && e.GroupId == groupId).CountAsync();

        return new PageResponse<ExpenseListResponse>(result, count, skip, take);
    }

    public async Task<bool> SaveExpenseAsync(Guid groupId, ExpenseRequest request, Guid userId)
    {
        var member = await memberService.GetMemberByUserIdAsync(userId)
            ?? throw new Exception("Member does not exist!");

        var groupMembers = await groupMemberService.GetAllGroupMembersDefaultAsync(groupId);
        var activeMembers = groupMembers
            .Where(x => x.GroupMemberStatus != GroupMemberStatus.Deleted && x.LeftAt is null);

        var activeMemberIds = activeMembers.Select(x => x.MemberId).ToHashSet();

        var gm = activeMembers
            .Where(e => e.Member.UserId == userId)
            .FirstOrDefault()
                ?? throw new Exception("Member is not in the group!");

        var expense = await CreateExpenseAsync(groupId, request, gm)
            ?? throw new Exception("Exception failed to create");

        await CreateExpensePaymentsAsync(expense, activeMemberIds, request);
        await CreateExpenseSplitsAsync(expense, activeMemberIds, request);

        await repo.SaveChangesAsync();
        return true;
    }
}
