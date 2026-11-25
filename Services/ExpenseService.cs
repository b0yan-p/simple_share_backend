using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Common;
using simpleShare_backend.Common.Currencies;
using simpleShare_backend.Services.Calculations;
using simpleShare_backend.Common.Exceptions;
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
    IGroupService groupService,
    IMemberService memberService,
    IGroupMemberService groupMemberService,
    IActivityWriter activityWriter,
    IActivityService activityService,
    IExchangeRateProvider exchangeRates
) : IExpenseService
{
    private static DateTime StoreDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    private static (Guid Id, decimal Amount)[] Payments(ExpenseFinancialInput input) => input.Payments.Select(x => (x.MemberId, x.OriginalAmount)).ToArray();
    private static (Guid Id, decimal Amount)[] Splits(ExpenseFinancialInput input) => input.Splits.Select(x => (x.MemberId, x.OriginalAmount)).ToArray();

    private async Task ValidateParticipantsAsync(Guid groupId, (Guid Id, decimal Amount)[] payments,
        (Guid Id, decimal Amount)[] splits, string currency, string target, bool settlement)
    {
        if (settlement && (currency != target || payments.Length != 1 || splits.Length != 1))
            throw new ValidationAppException("settlement_invalid", "A settlement uses group currency with one payer and one recipient.");
        if (settlement && payments[0].Id == splits[0].Id)
            throw new ValidationAppException("invalid_request", "You cannot pay to yourself!");
        var active = (await groupMemberService.GetAllGroupMembersDefaultAsync(groupId))
            .Where(x => x.LeftAt == null && x.GroupMemberStatus != GroupMemberStatus.Deleted).Select(x => x.MemberId).ToHashSet();
        if (payments.Concat(splits).Any(x => !active.Contains(x.Id)))
            throw new ValidationAppException("allocations_invalid_members", "Allocations contain inactive group members.");
    }

    private async Task ApplyFinancialAsync(Expense expense, ExpenseFinancialInput input, string target, bool reuseRate, bool settlement = false, CancellationToken cancellationToken = default)
    {
        if (input.ExpenseDate == default) throw new ValidationAppException("expense_date_invalid", "Expense date is required.");
        var currency = CurrencyCatalog.Validate(input.OriginalCurrency);
        if (input.Payments is null || input.Splits is null) throw new ValidationAppException("original_allocations_invalid", "Payments and splits are required.");
        var payments = Payments(input);
        var splits = Splits(input);
        ExpenseMoneyNormalizer.ValidateOriginal(input.OriginalAmount, payments, splits);
        await ValidateParticipantsAsync(expense.GroupId, payments, splits, currency, target, settlement);
        var observation = reuseRate ? new ExchangeRateObservation(expense.ExchangeRate, expense.ExchangeRateDate ?? input.ExpenseDate)
            : currency == target ? new ExchangeRateObservation(1m, input.ExpenseDate)
            : await exchangeRates.GetRateAsync(currency, target, input.ExpenseDate, cancellationToken);
        var total = ExpenseMoneyNormalizer.Total(input.OriginalAmount, observation.Rate);
        var normalizedPayments = ExpenseMoneyNormalizer.Allocate(payments, observation.Rate, total);
        var normalizedSplits = ExpenseMoneyNormalizer.Allocate(splits, observation.Rate, total);
        expense.TotalAmount = total;
        expense.OriginalAmount = input.OriginalAmount;
        expense.OriginalCurrency = currency;
        expense.ExchangeRate = observation.Rate;
        if (!reuseRate) expense.ExchangeRateDate = observation.Date;
        expense.ExpenseDate = StoreDate(input.ExpenseDate);
        var now = DateTime.UtcNow;
        foreach (var row in payments) expense.Payments.Add(new ExpensePayment { ExpenseId = expense.Id, PaidByMemberId = row.Id, OriginalAmount = row.Amount, Amount = normalizedPayments[row.Id], CreatedAt = now });
        foreach (var row in splits) expense.Splits.Add(new ExpenseSplit { ExpenseId = expense.Id, OwedByMemberId = row.Id, OriginalAmount = row.Amount, Amount = normalizedSplits[row.Id], CreatedAt = now });
    }

    public Task<ExpenseResponse> GetExpenseAsync(Guid groupId, Guid expenseId, Guid userId) =>
        ReadExpenseAsync(groupId, expenseId, userId, includeDeleted: false);

    private async Task<ExpenseResponse> ReadExpenseAsync(Guid groupId, Guid expenseId, Guid userId, bool includeDeleted)
    {
        var isMember = await groupMemberService.IsUserGroupMember(groupId, userId);

        if (!isMember)
            throw new ForbiddenAppException("forbidden", "Unauthorized access!");

        var expense = await db.Expenses
            .Where(e => e.Id == expenseId && e.GroupId == groupId && (includeDeleted || e.DeletedAt == null))
            .Include(e => e.Group)
            .Include(e => e.Payments)
                .ThenInclude(p => p.PaidByMember)
            .Include(e => e.Splits)
                .ThenInclude(p => p.OwedByMember)
            .Include(e => e.CreatedByMember)
            .Include(e => e.ModifiedByMember)
            .FirstOrDefaultAsync();

        if (expense == null)
            throw new NotFoundAppException("expense_not_found", "Expense not found!");

        return new ExpenseResponse()
        {
            Id = expense.Id,
            Amount = expense.TotalAmount,
            OriginalAmount = expense.OriginalAmount,
            OriginalCurrency = expense.OriginalCurrency,
            GroupCurrency = expense.Group.Currency,
            ExchangeRate = expense.ExchangeRate,
            ExchangeRateDate = expense.ExchangeRateDate,
            Description = expense.Description,
            ExpenseDate = DateOnly.FromDateTime(expense.ExpenseDate),
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
                Amount = p.Amount,
                OriginalAmount = p.OriginalAmount,
                MemberId = p.PaidByMemberId,
                MemberName = p.PaidByMember.DisplayName
            })],
            SplittedBy = [.. expense.Splits.Select(s => new ExpenseMemberAmountResponse()
            {
                Amount = s.Amount,
                OriginalAmount = s.OriginalAmount,
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
            ?? throw new NotFoundAppException("member_not_found", "Member not found!");

        _ = await groupMemberService.GetGroupMemberAsync(groupId, meMember.Id)
            ?? throw new ForbiddenAppException("not_group_member", "Not a group member!");

        var expenses = await db.Expenses
            .AsNoTracking()
            .Where(e => e.DeletedAt == null && e.GroupId == groupId)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Skip(skip)
            .Take(take)
            .Select(e => new
            {
                e.Id,
                e.Description,
                e.ExpenseDate,
                e.CreatedAt,
                e.TotalAmount,
                e.OriginalAmount,
                e.OriginalCurrency,
                GroupCurrency = e.Group.Currency,
                e.IsSettleUp,

                Payers = e.Payments.Select(p => new ExpensePayer(p.PaidByMemberId, p.PaidByMember.DisplayName)).ToList(),
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
            ExpenseDate = DateOnly.FromDateTime(e.ExpenseDate),
            CreatedAt = e.CreatedAt,
            IsSettleUp = e.IsSettleUp ?? false,
            Amount = e.TotalAmount,
            OriginalAmount = e.OriginalAmount,
            OriginalCurrency = e.OriginalCurrency,
            GroupCurrency = e.GroupCurrency,
            Payers = e.Payers,
            PaidByMembers = [.. e.Payers.Select(p => p.DisplayName)],
            Net = Math.Round(e.PaidSum - e.SplitSum, 2, MidpointRounding.AwayFromZero)
        })
        .ToList();

        var count = await db.Expenses.Where(e => e.DeletedAt == null && e.GroupId == groupId).CountAsync();

        return new PageResponse<ExpenseListResponse>(result, count, skip, take);
    }

    public async Task<ExpenseResponse> UpdateExpenseAsync(Guid groupId, UpdateExpenseRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await groupMemberService.IsUserGroupMember(groupId, userId))
            throw new ForbiddenAppException("forbidden", "Unauthorized access.");
        var member = await memberService.GetMemberByUserIdAsync(userId) ?? throw new NotFoundAppException("member_not_found", "Member not found.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var expense = await db.Expenses.FromSqlInterpolated($"SELECT * FROM \"Expenses\" WHERE \"Id\" = {request.Id} AND \"GroupId\" = {groupId} AND \"DeletedAt\" IS NULL FOR UPDATE")
            .SingleOrDefaultAsync() ?? throw new NotFoundAppException("expense_not_found", "Expense not found.");
        await db.Entry(expense).Collection(x => x.Payments).LoadAsync();
        await db.Entry(expense).Collection(x => x.Splits).LoadAsync();
        await db.Entry(expense).Reference(x => x.Group).LoadAsync();
        var oldExpense = (Expense)db.Entry(expense).OriginalValues.Clone().ToObject();
        var input = request.Financial;
        var allocationsChanged = false;
        if (input != null)
        {
            if (input.Payments is null || input.Splits is null) throw new ValidationAppException("original_allocations_invalid", "Payments and splits are required.");
            var currency = CurrencyCatalog.Validate(input.OriginalCurrency);
            var sameDate = input.ExpenseDate == DateOnly.FromDateTime(expense.ExpenseDate);
            var sameCurrency = currency == expense.OriginalCurrency;
            bool samePayments = Payments(input).OrderBy(x => x.Id).SequenceEqual(expense.Payments.Select(x => (x.PaidByMemberId, x.OriginalAmount)).OrderBy(x => x.PaidByMemberId));
            bool sameSplits = Splits(input).OrderBy(x => x.Id).SequenceEqual(expense.Splits.Select(x => (x.OwedByMemberId, x.OriginalAmount)).OrderBy(x => x.OwedByMemberId));
            allocationsChanged = !samePayments || !sameSplits;
            if (!sameDate || !sameCurrency || input.OriginalAmount != expense.OriginalAmount || !samePayments || !sameSplits)
            {
                // Validate and build the entire replacement before removing persisted children.
                var replacement = new Expense { Id = expense.Id, GroupId = groupId, ExchangeRate = expense.ExchangeRate, ExchangeRateDate = expense.ExchangeRateDate };
                await ApplyFinancialAsync(replacement, input, expense.Group.Currency, sameDate && sameCurrency, expense.IsSettleUp == true, cancellationToken);
                db.ExpensePayments.RemoveRange(expense.Payments);
                db.ExpenseSplits.RemoveRange(expense.Splits);
                await db.SaveChangesAsync();
                expense.Payments.Clear(); expense.Splits.Clear();
                expense.TotalAmount = replacement.TotalAmount; expense.OriginalAmount = replacement.OriginalAmount;
                expense.OriginalCurrency = replacement.OriginalCurrency; expense.ExchangeRate = replacement.ExchangeRate;
                expense.ExchangeRateDate = replacement.ExchangeRateDate; expense.ExpenseDate = replacement.ExpenseDate;
                foreach (var payment in replacement.Payments) expense.Payments.Add(payment);
                foreach (var split in replacement.Splits) expense.Splits.Add(split);
            }
            else if (expense.IsSettleUp == true)
            {
                await ValidateParticipantsAsync(groupId, Payments(input), Splits(input), currency, expense.Group.Currency, true);
            }
        }
        if (request.Description is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Description)) throw new ValidationAppException("invalid_request", "Description is required.");
            expense.Description = request.Description.Trim();
        }
        expense.ModifiedAt = DateTime.UtcNow; expense.ModifiedByMemberId = member.Id;
        activityWriter.Enqueue(activityService.AddExpenseUpdatedActivity(oldExpense, expense, member.Id, allocationsChanged));
        await db.SaveChangesAsync();
        var result = await GetExpenseAsync(groupId, expense.Id, userId);
        await tx.CommitAsync();
        return result;
    }

    public Task<ExpenseResponse> SaveExpenseAsync(Guid groupId, CreateExpenseRequest request, Guid userId, CancellationToken cancellationToken = default) => SaveCoreAsync(groupId, request, userId, false, cancellationToken);

    // Keep the original create identity stable across subsequent edits and soft deletion.
    private static byte[] CreateFingerprint(CreateExpenseRequest request)
    {
        static string Money(decimal amount) => amount.ToString("G29", CultureInfo.InvariantCulture);
        static object Rows((Guid Id, decimal Amount)[] rows) =>
            rows.OrderBy(x => x.Id).Select(x => new { x.Id, Amount = Money(x.Amount) });
        if (request.Payments is null || request.Splits is null)
            throw new ValidationAppException("original_allocations_invalid", "Payments and splits are required.");
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            Description = request.Description.Trim(), request.ExpenseDate,
            Currency = CurrencyCatalog.Validate(request.OriginalCurrency), Amount = Money(request.OriginalAmount),
            Payments = Rows(Payments(request)), Splits = Rows(Splits(request))
        }));
    }

    private static ConflictAppException CreateConflict() =>
        new("expense_create_conflict", "This create identifier already belongs to a different expense request.");

    private async Task<ExpenseResponse> SaveCoreAsync(Guid groupId, CreateExpenseRequest request, Guid userId, bool settlement, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Description)) throw new ValidationAppException("invalid_request", "Description is required.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var group = await db.Groups.FromSqlInterpolated($"SELECT * FROM \"Groups\" WHERE \"Id\" = {groupId} AND \"DeletedAt\" IS NULL FOR UPDATE")
            .SingleOrDefaultAsync() ?? throw new NotFoundAppException("group_not_found", "Group not found.");
        await db.Entry(group).ReloadAsync(cancellationToken);
        if (settlement) request.OriginalCurrency = group.Currency;
        var members = await groupMemberService.GetAllGroupMembersDefaultAsync(groupId);
        var owner = members.FirstOrDefault(x => x.LeftAt == null && x.GroupMemberStatus != GroupMemberStatus.Deleted && x.Member.UserId == userId)
            ?? throw new ForbiddenAppException("member_not_in_group", "Not an active group member.");
        var id = settlement ? Guid.NewGuid() : request.ClientExpenseId;
        if (id == Guid.Empty)
            throw new ValidationAppException("client_expense_id_required", "A client expense identifier is required.");
        var fingerprint = settlement ? null : CreateFingerprint(request);
        if (!settlement)
        {
            var existing = await db.Expenses.AsNoTracking().Where(e => e.Id == id)
                .Select(e => new { e.GroupId, e.CreatedByMemberId, e.CreateFingerprint })
                .SingleOrDefaultAsync(cancellationToken);
            if (existing != null)
            {
                if (existing.GroupId != groupId || existing.CreatedByMemberId != owner.MemberId ||
                    existing.CreateFingerprint == null || !existing.CreateFingerprint.SequenceEqual(fingerprint!))
                    throw CreateConflict();
                return await ReadExpenseAsync(groupId, id, userId, includeDeleted: true);
            }
        }
        var expense = new Expense { Id = id, CreateFingerprint = fingerprint, GroupId = groupId, Group = group, Description = request.Description.Trim(), CreatedAt = DateTime.UtcNow, CreatedByMemberId = owner.MemberId, IsSettleUp = settlement };
        await ApplyFinancialAsync(expense, request, group.Currency, false, settlement, cancellationToken);
        db.Expenses.Add(expense);
        if (settlement)
            activityWriter.Enqueue(new ActivityEvent<SettleUpActivityData>(Type: ActivityType.SettleUpCreated, EntityType: ActivityEntityType.SettleUp, EntityId: expense.Id, GroupId: groupId, ActorMemberId: owner.MemberId,
                Data: new SettleUpActivityData(expense.Payments.Single().PaidByMemberId, expense.Splits.Single().OwedByMemberId, expense.TotalAmount)));
        else
            activityWriter.Enqueue(new ActivityEvent<ExpenseActivityData>(Type: ActivityType.ExpenseCreated, EntityType: ActivityEntityType.Expense, EntityId: expense.Id, GroupId: groupId, ActorMemberId: owner.MemberId,
                Data: new ExpenseActivityData(expense.Description, expense.TotalAmount)));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
               { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_Expenses" })
        {
            // Different groups hold different row locks, but expense ids are globally unique.
            throw CreateConflict();
        }
        var result = await GetExpenseAsync(groupId, expense.Id, userId);
        await tx.CommitAsync();
        return result;
    }

    public async Task<bool> DeleteExpenseAsync(Guid groupId, Guid expenseId, Guid userId)
    {
        var isMember = await groupMemberService.IsUserGroupMember(groupId, userId);

        if (!isMember)
            throw new ForbiddenAppException("not_group_member", "You are not group member!");

        var member = await memberService.GetMemberByUserIdAsync(userId)
            ?? throw new NotFoundAppException("member_not_found", "Member not found!");

        var expense = await db.Expenses
            .Where(e => e.GroupId == groupId && e.Id == expenseId && e.DeletedAt == null)
            .FirstOrDefaultAsync()
            ?? throw new NotFoundAppException("expense_not_found", "Expense not found!");

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

    public async Task<bool> SettleUpDebtAsync(Guid groupId, SettleUpRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        Member fromMember;
        Member toMember;
        if (request.FromMemberId is null)
        {
            fromMember = await memberService.GetMemberByUserIdAsync(userId)
                ?? throw new NotFoundAppException("member_not_found", "Member not found!");
            request.FromMemberId = fromMember.Id;
        }
        else
        {
            fromMember = await memberService.GetMemberAsync(request.FromMemberId.Value)
                ?? throw new NotFoundAppException("member_not_found", "Member not found!");
        }

        var isGroupMemberA = await groupMemberService.IsGroupMemberAsync(groupId, request.FromMemberId.Value);
        var isGroupMemberB = await groupMemberService.IsGroupMemberAsync(groupId, request.ToMemberId);

        if (!isGroupMemberA || !isGroupMemberB)
            throw new ForbiddenAppException("member_not_in_group", "Group member is not in group");

        toMember = await memberService.GetMemberAsync(request.ToMemberId)
            ?? throw new NotFoundAppException("member_not_found", "Member not found!");

        var amount = request.Amount;

        CreateExpenseRequest expRequest = new()
        {
            Description = $"{fromMember.DisplayName} paid to {toMember.DisplayName} {amount}",
            ExpenseDate = request.Date,
            OriginalAmount = amount,
            OriginalCurrency = (await groupService.GetGroupAsync(groupId)).Currency,
            Payments = [new ExpensePaymentRequest(){
                MemberId = request.FromMemberId.Value,
                OriginalAmount = amount
            }],
            Splits = [new SplitRequest(){
                MemberId = request.ToMemberId,
                OriginalAmount = amount
            }]
        };

        await SaveCoreAsync(groupId, expRequest, userId, true, cancellationToken);

        return true;
    }

}
