using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Data;
using simpleShare_backend.Models.Application;
using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Services;

public class BalanceService(
    ApplicationDbContext db,
    IMemberService memberService
) : IBalanceService
{
    public async Task<GroupBalances> GetGroupBalancesAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var myMember = await memberService.GetMemberByUserIdAsync(userId)
            ?? throw new Exception("Member does not exist");

        var isActive = await db.GroupMembers
            .AsNoTracking()
            .Where(gm =>
                gm.GroupId == groupId
                && gm.MemberId == myMember.Id
                && gm.LeftAt == null
                && gm.GroupMemberStatus != GroupMemberStatus.Deleted
            )
            .AnyAsync(cancellationToken: ct);

        if (!isActive)
            throw new Exception("You are not an active member of this group.");

        // TODO what we gonna do with members that have some debt but they are not active???
        var activeMembers = await db.GroupMembers
            .AsNoTracking()
            .Where(
                x => x.GroupId == groupId
                && x.LeftAt == null
                && x.GroupMemberStatus != GroupMemberStatus.Deleted
            )
            .Select(x => new
            {
                x.MemberId,
                x.Member.DisplayName
            })
            .ToListAsync(ct);

        var activeIds = activeMembers
            .Select(x => x.MemberId)
            .ToHashSet();

        var expensesInGroup = db.Expenses
            .AsNoTracking()
            .Where(e => e.GroupId == groupId && e.DeletedAt == null);

        // Paid totals by group member
        var paidRows = await db.ExpensePayments
            .AsNoTracking()
            .Where(x => activeIds.Contains(x.PaidByMemberId))
            .Join(
                expensesInGroup,
                p => p.ExpenseId,
                e => e.Id,
                (p, e) => p
            )
            .GroupBy(p => p.PaidByMemberId)
            .Select(g => new { MemberId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken: ct);

        var paid = paidRows.ToDictionary(x => x.MemberId, x => x.Amount);

        // Split totals by group members
        var owedRows = await db.ExpenseSplits
            .AsNoTracking()
            .Include(x => x.Expense)
            .Where(s =>
                activeIds.Contains(s.OwedByMemberId)
                && s.Expense.GroupId == groupId
                && s.Expense.DeletedAt == null
            )
            .GroupBy(s => s.OwedByMemberId)
            .Select(s => new { MemberId = s.Key, Amount = s.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken: ct);

        var owed = owedRows.ToDictionary(x => x.MemberId, x => x.Amount);

        // Merge into member balances
        var members = activeMembers
            .Select(m =>
            {
                paid.TryGetValue(m.MemberId, out var p);
                owed.TryGetValue(m.MemberId, out var o);

                p = Math.Round(p, 2, MidpointRounding.AwayFromZero);
                o = Math.Round(o, 2, MidpointRounding.AwayFromZero);

                return new MemberBalance
                {
                    MemberId = m.MemberId,
                    DisplayName = m.DisplayName,
                    Paid = p,
                    Owed = o
                };
            })
            .OrderByDescending(x => x.Net)
            .ToList();

        return new GroupBalances
        {
            GroupId = groupId,
            Members = members
        };
    }
}
