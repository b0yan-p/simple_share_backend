using System.Data;
using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Data;
using simpleShare_backend.Models.Application;
using simpleShare_backend.Models.Enums;
using simpleShare_backend.Services.Calculations;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SimpleShare.UnitTests")]

namespace simpleShare_backend.Services;

public class BalanceService(ApplicationDbContext db, IMemberService memberService, ILogger<BalanceService> logger) : IBalanceService
{
    public async Task<GroupBalances> GetGroupBalancesAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var member = await memberService.GetMemberByUserIdAsync(userId)
            ?? throw new NotFoundAppException("member_not_found", "Member does not exist!");
        if (!await db.GroupMembers.AsNoTracking().AnyAsync(x => x.GroupId == groupId
            && x.MemberId == member.Id && x.LeftAt == null && x.GroupMemberStatus != GroupMemberStatus.Deleted, ct))
            throw new NotFoundAppException("member_not_found", "Member not found in group");
        var groups = await GetGroupsBalancesAsync([groupId], ct);
        var group = groups.GetValueOrDefault(groupId)
            ?? throw new NotFoundAppException("group_not_found", "Group does not exist!");
        return new GroupBalances
        {
            GroupId = groupId,
            Members = group.Members.OrderByDescending(x => x.MemberId == member.Id)
                .ThenByDescending(x => x.Net).ThenBy(x => x.MemberId).ToArray(),
            Debts = group.Debts
        };
    }

    /// <summary>Loads a bounded set of groups in batches, using one consistent database snapshot.</summary>
    public async Task<IReadOnlyDictionary<Guid, GroupBalances>> GetGroupsBalancesAsync(
        IReadOnlyCollection<Guid> groupIds, CancellationToken ct = default)
    {
        if (groupIds.Count == 0) return new Dictionary<Guid, GroupBalances>();
        // A concurrent expense edit must not mix old payments with new splits across queries.
        var ownsTransaction = db.Database.CurrentTransaction == null;
        await using var snapshot = ownsTransaction
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
        var groups = await db.Groups.AsNoTracking().Where(x => groupIds.Contains(x.Id) && x.DeletedAt == null)
            .Select(x => new { x.Id, x.SimplifyDebts }).ToListAsync(ct);
        var ids = groups.Select(x => x.Id).ToArray();
        var expenses = await db.Expenses.AsNoTracking().Where(x => ids.Contains(x.GroupId) && x.DeletedAt == null)
            .Select(x => new { x.Id, x.GroupId, x.TotalAmount }).ToListAsync(ct);
        var payments = await db.ExpensePayments.AsNoTracking()
            .Where(x => ids.Contains(x.Expense.GroupId) && x.Expense.DeletedAt == null)
            .Select(x => new { x.ExpenseId, Id = x.PaidByMemberId, x.Amount }).ToListAsync(ct);
        var splits = await db.ExpenseSplits.AsNoTracking()
            .Where(x => ids.Contains(x.Expense.GroupId) && x.Expense.DeletedAt == null)
            .Select(x => new { x.ExpenseId, Id = x.OwedByMemberId, x.Amount }).ToListAsync(ct);
        var financialIds = payments.Select(x => x.Id).Concat(splits.Select(x => x.Id)).Distinct().ToArray();
        var members = await db.GroupMembers.AsNoTracking()
            .Where(x => ids.Contains(x.GroupId) && ((x.LeftAt == null && x.GroupMemberStatus != GroupMemberStatus.Deleted)
                || financialIds.Contains(x.MemberId)))
            .Select(x => new
            {
                x.GroupId, Id = x.MemberId, x.Member.DisplayName,
                Active = x.LeftAt == null && x.GroupMemberStatus != GroupMemberStatus.Deleted
            }).ToListAsync(ct);
        var paymentLookup = payments.ToLookup(x => x.ExpenseId);
        var splitLookup = splits.ToLookup(x => x.ExpenseId);
        var expenseLookup = expenses.ToLookup(x => x.GroupId);
        var memberLookup = members.ToLookup(x => x.GroupId);
        var result = new Dictionary<Guid, GroupBalances>();
        foreach (var group in groups)
        {
            var relevantIds = expenseLookup[group.Id]
                .SelectMany(x => paymentLookup[x.Id].Select(p => p.Id).Concat(splitLookup[x.Id].Select(s => s.Id)))
                .ToHashSet();
            var names = memberLookup[group.Id].Where(x => x.Active || relevantIds.Contains(x.Id))
                .ToDictionary(x => x.Id, x => x.DisplayName);
            var paid = names.Keys.ToDictionary(x => x, _ => 0m);
            var owed = names.Keys.ToDictionary(x => x, _ => 0m);
            var raw = new Dictionary<(Guid Debtor, Guid Creditor), decimal>();
            try
            {
                foreach (var expense in expenseLookup[group.Id])
                {
                    var payers = paymentLookup[expense.Id].OrderBy(x => x.Id).ToArray();
                    var shares = splitLookup[expense.Id].OrderBy(x => x.Id).ToArray();
                    foreach (var edge in BuildExpenseLedger(expense.TotalAmount,
                        payers.Select(x => (x.Id, x.Amount)).ToArray(),
                        shares.Select(x => (x.Id, x.Amount)).ToArray()))
                    {
                        if (edge.Amount > 0)
                        {
                            var key = (edge.Debtor, edge.Creditor);
                            raw[key] = raw.GetValueOrDefault(key) + edge.Amount;
                        }
                    }
                    foreach (var p in payers) paid[p.Id] += p.Amount;
                    foreach (var s in shares) owed[s.Id] += s.Amount;
                }
                var calculated = DebtGraphCalculator.Calculate(
                    names.Keys.Select(x => new DebtGraphCalculator.Member(x, paid[x], owed[x])),
                    raw.Select(x => new DebtGraphCalculator.RawDebt(x.Key.Debtor, x.Key.Creditor, x.Value)));
                result[group.Id] = new GroupBalances
                {
                    GroupId = group.Id,
                    Members = calculated.Members.Select(x => new MemberBalance
                    {
                        MemberId = x.Id, DisplayName = names[x.Id], Paid = x.Paid / 100m, Owed = x.Owed / 100m,
                        Side = x.Net > 0 ? BalanceSide.Creditor : x.Net < 0 ? BalanceSide.Debtor : BalanceSide.Settled
                    }).ToArray(),
                    Debts = (group.SimplifyDebts ? calculated.Simplified : calculated.Regular).Select(x => new DebtEdge
                    {
                        DebtorMemberId = x.Debtor, DebtorDisplayName = names[x.Debtor],
                        CreditorMemberId = x.Creditor, CreditorDisplayName = names[x.Creditor], Amount = x.Cents / 100m
                    }).ToArray()
                };
            }
            catch (Exception ex) when (ex is ArgumentException or OverflowException or KeyNotFoundException)
            {
                logger.LogError(ex, "Financial data is inconsistent for group {GroupId}", group.Id);
                throw new ConflictAppException("financial_data_inconsistent", "Financial data is inconsistent. Please contact support.");
            }
        }
        if (snapshot != null) await snapshot.CommitAsync(ct);
        return result;
    }

    // The adapter preserves exact row/column totals before the calculator's cent projection.
    // Eight decimal places bound intermediate division residues, without rounding stored money.
    internal static IReadOnlyList<DebtGraphCalculator.RawDebt> BuildExpenseLedger(decimal total,
        IReadOnlyList<(Guid Id, decimal Amount)> payments, IReadOnlyList<(Guid Id, decimal Amount)> splits)
    {
        var payers = payments.OrderBy(x => x.Id).ToArray();
        var shares = splits.OrderBy(x => x.Id).ToArray();
        if (total <= 0 || payers.Length == 0 || shares.Length == 0
            || payers.Any(x => x.Amount <= 0) || shares.Any(x => x.Amount <= 0)
            || payers.Select(x => x.Id).Distinct().Count() != payers.Length
            || shares.Select(x => x.Id).Distinct().Count() != shares.Length
            || payers.Sum(x => x.Amount) != total || shares.Sum(x => x.Amount) != total)
            throw new ArgumentException("Expense has inconsistent payments or splits.");
        var result = new List<DebtGraphCalculator.RawDebt>();
        var remaining = payers.Select(x => x.Amount).ToArray();
        var totalRemaining = total;
        foreach (var share in shares)
        {
            var row = share.Amount;
            for (var i = 0; i < payers.Length; i++)
            {
                var amount = share.Amount == totalRemaining ? remaining[i]
                    : Math.Min(row, Math.Min(remaining[i],
                        decimal.Floor(share.Amount * (remaining[i] / totalRemaining) * 100000000m) / 100000000m));
                result.Add(new(share.Id, payers[i].Id, amount));
                remaining[i] -= amount;
                row -= amount;
            }
            // Deterministically allocate division residues within the same expense, never
            // adding/subtracting source money or accepting mismatched source totals.
            for (var i = 0; row > 0 && i < payers.Length; i++)
            {
                var amount = Math.Min(row, remaining[i]);
                result.Add(new(share.Id, payers[i].Id, amount));
                remaining[i] -= amount;
                row -= amount;
            }
            totalRemaining -= share.Amount;
            if (row != 0 || remaining.Any(x => x < 0))
                throw new ArgumentException("Proportional ledger allocation failed.");
        }
        return result;
    }

    public async Task<OverallBalance> GetMyOverallBalanceAsync(Guid userId, CancellationToken ct = default)
    {
        var member = await memberService.GetMemberByUserIdAsync(userId);
        if (member == null) return new OverallBalance();
        var ids = await db.GroupMembers.AsNoTracking().Where(x => x.MemberId == member.Id && x.LeftAt == null
            && x.GroupMemberStatus != GroupMemberStatus.Deleted && x.Group.DeletedAt == null)
            .Select(x => x.GroupId).ToArrayAsync(ct);
        var groups = await GetGroupsBalancesAsync(ids, ct);
        var result = new OverallBalance();
        foreach (var group in groups.Values)
        {
            var relevant = group.Debts.Where(x => x.DebtorMemberId == member.Id || x.CreditorMemberId == member.Id).ToArray();
            result.OwedToMeTotal += relevant.Where(x => x.CreditorMemberId == member.Id).Sum(x => x.Amount);
            result.IOweTotal += relevant.Where(x => x.DebtorMemberId == member.Id).Sum(x => x.Amount);
            if (relevant.Length > 0) result.GroupCount++;
        }
        return result;
    }

    public async Task<(decimal paid, decimal owed, decimal net)> GetMemberNetInGroupAsync(Guid groupId, Guid memberId)
    {
        var groups = await GetGroupsBalancesAsync([groupId]);
        var group = groups.GetValueOrDefault(groupId)
            ?? throw new NotFoundAppException("group_not_found", "Group does not exist!");
        var balance = group.Members.SingleOrDefault(x => x.MemberId == memberId);
        return balance == null ? (0m, 0m, 0m) : (balance.Paid, balance.Owed, balance.Net);
    }
}
