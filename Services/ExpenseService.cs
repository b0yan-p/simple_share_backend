using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Common;
using simpleShare_backend.Contracts.Expenses;
using simpleShare_backend.Data;
using simpleShare_backend.Infrastructure;
using simpleShare_backend.Models.Application.Activity;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Enums;
using simpleShare_backend.Services.Interfaces;

namespace simpleShare_backend.Services;

public class ExpenseService(
    ApplicationDbContext db,
    IRepositoryAsync repo,
    IGroupService groupService,
    IMemberService memberService,
    IGroupMemberService groupMemberService,
    IActivityWriter activityWriter,
    IActivityService activityService
) : IExpenseService
{
    public async Task<Expense> CreateExpenseAsync(Guid groupId, CreateExpenseRequest request, GroupMember owner)
    {
        ValidateCreateExpenseRequest(request);

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
            CreatedByMemberId = owner.MemberId,
            IsSettleUp = request.IsSettleUp,
        };

        return await repo.CreateAsync<Expense, Guid>(expense);
    }

    public async Task<ICollection<ExpensePayment>> CreateExpensePaymentsAsync(
        Expense expense,
        IReadOnlySet<Guid> activeMemberIds,
        ICollection<ExpensePaymentRequest> payments
    )
    {
        if (payments is null || payments.Count == 0)
            throw new Exception("Payments are required!");

        if (payments.Any(p => p.Amount <= 0))
            throw new Exception("Payment amount must be greater than 0");

        var paymentMemberIds = payments
            .Select(x => x.MemberId)
            .ToList();

        if (paymentMemberIds.Distinct().Count() != payments.Count)
            throw new Exception("Payment list must be unique");

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
        ICollection<SplitRequest> splits
    )
    {
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

    public async Task<ExpenseResponse> GetExpenseAsync(Guid groupId, Guid expenseId, Guid userId)
    {
        var isMember = await groupMemberService.IsUserGroupMember(groupId, userId);

        if (!isMember)
            throw new Exception("Unauthorized access!");

        var expense = await db.Expenses
            .Where(e => e.Id == expenseId && e.GroupId == groupId && e.DeletedAt == null)
            .Include(e => e.Payments)
                .ThenInclude(p => p.PaidByMember)
            .Include(e => e.Splits)
                .ThenInclude(p => p.OwedByMember)
            .Include(e => e.CreatedByMember)
            .Include(e => e.ModifiedByMember)
            .FirstOrDefaultAsync();

        if (expense == null)
            throw new Exception("Expense not found!");

        return new ExpenseResponse()
        {
            Id = expense.Id,
            Amount = Math.Round(expense.TotalAmount, 2, MidpointRounding.AwayFromZero),
            Description = expense.Description,
            ExpenseDate = expense.ExpenseDate,
            IsSettleUp = expense.IsSettleUp ?? false,
            CreatedInfo = new ExpenseLogResponse()
            {
                MemberId = expense.CreatedByMemberId,
                MemberName = expense.CreatedByMember.DisplayName,
                ModificationDate = expense.CreatedAt
            },
            ModifiedInfo = expense.ModifiedByMemberId == null ? null : new ExpenseLogResponse()
            {
                MemberId = expense.ModifiedByMemberId.Value,
                MemberName = expense.ModifiedByMember.DisplayName,
                ModificationDate = expense.ModifiedAt!.Value
            },
            PaidBy = [.. expense.Payments.Select(p => new ExpenseMemberAmountResponse()
            {
                Amount = Math.Round(p.Amount, 2, MidpointRounding.AwayFromZero),
                MemberId = p.PaidByMemberId,
                MemberName = p.PaidByMember.DisplayName
            })],
            SplittedBy = [.. expense.Splits.Select(s => new ExpenseMemberAmountResponse()
            {
                Amount = Math.Round(s.Amount, 2, MidpointRounding.AwayFromZero),
                MemberId = s.OwedByMemberId,
                MemberName = s.OwedByMember.DisplayName
            })]
        };
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
                e.IsSettleUp,

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
            IsSettleUp = e.IsSettleUp ?? false,
            Amount = Math.Round(e.TotalAmount, 2, MidpointRounding.AwayFromZero),
            PaidByMembers = [.. e.PaidByMembers],
            Net = Math.Round(e.PaidSum - e.SplitSum, 2, MidpointRounding.AwayFromZero)
        })
        .ToList();

        var count = await db.Expenses.Where(e => e.DeletedAt == null && e.GroupId == groupId).CountAsync();

        return new PageResponse<ExpenseListResponse>(result, count, skip, take);
    }

    public async Task<ExpenseResponse> UpdateExpenseAsync(Guid groupId, UpdateExpenseRequest request, Guid userId)
    {
        ValidateUpdateExpenseRequest(request);

        var meMember = await memberService.GetMemberByUserIdAsync(userId)
            ?? throw new Exception("Member not found in group");

        var isMember = await groupMemberService.IsUserGroupMember(groupId, userId);
        if (!isMember)
            throw new Exception("Unauthorized access!");

        var dbExpense = await db.Expenses
            .Where(x => x.Id == request.Id && x.GroupId == groupId && x.DeletedAt == null)
            .FirstOrDefaultAsync()
            ?? throw new Exception("Expense not found!");

        await using var tx = await db.Database.BeginTransactionAsync();

        var oldExpense = (Expense)db.Entry(dbExpense).OriginalValues.Clone().ToObject();

        dbExpense.ModifiedAt = DateTime.UtcNow;
        dbExpense.Description = request.Description ?? dbExpense.Description;
        dbExpense.ExpenseDate = request.ExpenseDate ?? dbExpense.ExpenseDate;
        dbExpense.ModifiedByMemberId = meMember.Id;
        dbExpense.TotalAmount = request.TotalAmount ?? dbExpense.TotalAmount;

        var groupMembers = await groupMemberService.GetAllGroupMembersDefaultAsync(groupId);
        var activeMemberIds = groupMembers
                    .Where(x =>
                        x.GroupMemberStatus != GroupMemberStatus.Deleted &&
                        x.LeftAt is null
                    )
                    .Select(x => x.MemberId)
                    .ToHashSet();

        if (request.Payments != null)
        {
            await DeleteExpensePaymentsAsync(dbExpense.Id);
            await CreateExpensePaymentsAsync(dbExpense, activeMemberIds, request.Payments);
        }

        if (request.Splits != null)
        {
            await DeleteExpenseSplitsAsync(dbExpense.Id);
            await CreateExpenseSplitsAsync(dbExpense, activeMemberIds, request.Splits);
        }

        var activity = activityService.AddExpenseUpdatedActivity(oldExpense, dbExpense, meMember.Id);
        activityWriter.Enqueue(activity);

        await repo.SaveChangesAsync();
        var result = await GetExpenseAsync(groupId, dbExpense.Id, userId);
        await tx.CommitAsync();

        return result;
    }

    public async Task<ExpenseResponse> SaveExpenseAsync(Guid groupId, CreateExpenseRequest request, Guid userId)
    {
        ValidateCreateExpenseRequest(request);

        _ = await memberService.GetMemberByUserIdAsync(userId)
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


        await CreateExpensePaymentsAsync(expense, activeMemberIds, request.Payments);
        await CreateExpenseSplitsAsync(expense, activeMemberIds, request.Splits);

        if (request.IsSettleUp != null && request.IsSettleUp.Value)
        {
            var activity = new ActivityEvent<SettleUpActivityData>(
                Type: ActivityType.SettleUpCreated,
                EntityType: ActivityEntityType.SettleUp,
                EntityId: expense.Id,
                GroupId: groupId,
                ActorMemberId: gm.MemberId,
                Data: new SettleUpActivityData(
                    FromMemberId: expense.Payments.FirstOrDefault()!.PaidByMemberId,
                    ToMemberId: expense.Splits.FirstOrDefault()!.OwedByMemberId,
                    Amount: expense.TotalAmount
                )
            );
            activityWriter.Enqueue(activity);
        }
        else
        {
            var activityEvent = new ActivityEvent<ExpenseActivityData>(
                Type: ActivityType.ExpenseCreated,
                EntityType: ActivityEntityType.Expense,
                EntityId: expense.Id,
                GroupId: groupId,
                ActorMemberId: gm.MemberId,
                Data: new ExpenseActivityData(
                    Description: expense.Description,
                    Amount: expense.TotalAmount
                )
            );
            activityWriter.Enqueue(activityEvent);
        }

        await repo.SaveChangesAsync();

        return await GetExpenseAsync(groupId, expense.Id, userId);
    }

    public async Task<bool> DeleteExpenseAsync(Guid groupId, Guid expenseId, Guid userId)
    {
        var isMember = await groupMemberService.IsUserGroupMember(groupId, userId);

        if (!isMember)
            throw new Exception("You are not group member!");

        var member = await memberService.GetMemberByUserIdAsync(userId)
            ?? throw new Exception("Member not found!");

        var expense = await db.Expenses
            .Where(e => e.GroupId == groupId && e.Id == expenseId && e.DeletedAt == null)
            .FirstOrDefaultAsync()
            ?? throw new Exception("Expense not found!");

        expense.DeletedAt = DateTime.UtcNow;
        expense.ModifiedAt = DateTime.UtcNow;

        var activityEvent = new ActivityEvent<ExpenseActivityData>(
            Type: ActivityType.ExpenseDeleted,
            EntityType: ActivityEntityType.Expense,
            EntityId: expense.Id,
            GroupId: groupId,
            ActorMemberId: member.Id,
            Data: new ExpenseActivityData(
                Description: expense.Description,
                Amount: expense.TotalAmount
            )
        );

        activityWriter.Enqueue(activityEvent);

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> SettleUpDebtAsync(Guid groupId, SettleUpRequest request, Guid userId)
    {
        Member fromMember;
        Member toMember;
        if (request.FromMemberId is null)
        {
            fromMember = await memberService.GetMemberByUserIdAsync(userId)
                ?? throw new Exception("Member not found!");
            request.FromMemberId = fromMember.Id;
        }
        else
        {
            fromMember = await memberService.GetMemberAsync(request.FromMemberId.Value)
                ?? throw new Exception("Member not found!");
        }

        if (request.FromMemberId == request.ToMemberId)
            throw new Exception("You cannot pay to yourself!");

        var isGroupMemberA = await groupMemberService.IsGroupMemberAsync(groupId, request.FromMemberId.Value);
        var isGroupMemberB = await groupMemberService.IsGroupMemberAsync(groupId, request.ToMemberId);

        if (!isGroupMemberA || !isGroupMemberB)
            throw new Exception("Group member is not in group");

        toMember = await memberService.GetMemberAsync(request.ToMemberId)
            ?? throw new Exception("Member not found!");

        var amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);

        CreateExpenseRequest expRequest = new()
        {
            Description = $"{fromMember.DisplayName} paid to {toMember.DisplayName} {amount}",
            ExpenseDate = request.Date,
            TotalAmount = amount,
            IsSettleUp = true,
            Payments = [new ExpensePaymentRequest(){
                MemberId = request.FromMemberId.Value,
                Amount = amount
            }],
            Splits = [new SplitRequest(){
                MemberId = request.ToMemberId,
                Amount = amount
            }]
        };

        await SaveExpenseAsync(groupId, expRequest, userId);

        return true;
    }

    private void ValidateCreateExpenseRequest(CreateExpenseRequest request)
    {
        if (string.IsNullOrEmpty(request.Description))
            throw new Exception("Description is required");

        if (request.ExpenseDate == default)
            throw new Exception("Expense date is required");

        if (request.TotalAmount <= 0)
            throw new Exception("Total amount must be greater than 0");

        if (request.IsSettleUp != null && request.IsSettleUp.Value && request.Payments.Count > 1)
            throw new Exception("Settle up can be done only by one payer at the time!");

        if (request.IsSettleUp != null && request.IsSettleUp.Value && request.Splits.Count > 1)
            throw new Exception("Settle up can be done only to one member at the time!");
    }

    private void ValidateUpdateExpenseRequest(UpdateExpenseRequest request)
    {
        if (request.ExpenseDate != null && request.ExpenseDate == default)
            throw new Exception("Expense date is required");

        if (request.TotalAmount != null && request.TotalAmount <= 0)
            throw new Exception("Total amount must be greater than 0");

        if (request.TotalAmount > 0 && (request.Payments == null || request.Payments.Count == 0 || request.Splits == null))
            throw new Exception("Please enter payments and split shares!");
    }

    private async Task DeleteExpensePaymentsAsync(Guid expenseId)
    {
        await db.ExpensePayments
            .Where(ep => ep.ExpenseId == expenseId)
            .ExecuteDeleteAsync();
    }

    private async Task DeleteExpenseSplitsAsync(Guid expenseId)
    {
        await db.ExpenseSplits
            .Where(es => es.ExpenseId == expenseId)
            .ExecuteDeleteAsync(); ;
    }
}
