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
            )
            .AnyAsync(cancellationToken: ct);

        if (!isActive)
            throw new Exception("You are not an active member of this group.");

        var activeMembers = await db.GroupMembers
            .AsNoTracking()
            .Where(
                x => x.GroupId == groupId
            )
            .Select(x => new
            {
                x.MemberId,
                x.Member.DisplayName,
            })
            .ToListAsync(ct);

        var names = activeMembers.ToDictionary(x => x.MemberId, x => x.DisplayName);

        var expensesInGroup = db.Expenses
            .AsNoTracking()
            .Where(e => e.GroupId == groupId && e.DeletedAt == null);

        // Paid totals by group member
        var paidRows = await db.ExpensePayments
        .AsNoTracking()
        .Where(p =>
            p.Expense.GroupId == groupId
            && p.Expense.DeletedAt == null
        )
        .GroupBy(p => p.PaidByMemberId)
        .Select(g => new
        {
            MemberId = g.Key,
            Amount = g.Sum(x => x.Amount)
        })
        .ToListAsync(ct);

        var paid = paidRows.ToDictionary(x => x.MemberId, x => x.Amount);

        // Split totals by group members
        var owedRows = await db.ExpenseSplits
            .AsNoTracking()
            .Where(s =>
                s.Expense.GroupId == groupId
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
                    Owed = o,
                    Side = p - o > 0 ? BalanceSide.Creditor
                        : p - o < 0 ? BalanceSide.Debtor
                        : BalanceSide.Settled
                };
            })
            .OrderByDescending(x => x.Net)
            .ToList();

        var totalPaidQuery = db.ExpensePayments
            .AsNoTracking()
            .Where(p =>
                p.Expense.GroupId == groupId &&
                p.Expense.DeletedAt == null
            )
            .GroupBy(p => p.ExpenseId)
            .Select(g => new
            {
                ExpenseId = g.Key,
                TotalPaid = g.Sum(x => x.Amount)
            })
            .Where(x => x.TotalPaid > 0);

        // Debts / Global Ledger
        var debtRows = await db.ExpenseSplits
            .AsNoTracking()
            .Where(s =>
                // activeIds.Contains(s.OwedByMemberId) &&
                s.Expense.GroupId == groupId
                && s.Expense.DeletedAt == null
            ).Join(
                db.ExpensePayments
                    .AsNoTracking()
                    .Where(p =>
                        p.Expense.GroupId == groupId &&
                        p.Expense.DeletedAt == null
                    ),
                s => s.ExpenseId,
                p => p.ExpenseId,
                (s, p) => new { s, p }
            )
            .Join(
                totalPaidQuery,
                sp => sp.p.ExpenseId,
                t => t.ExpenseId,
                (sp, t) => new { sp.s, sp.p, t.TotalPaid }
            )
            .Where(x => x.s.OwedByMemberId != x.p.PaidByMemberId)
            .GroupBy(x => new
            {
                DebtorId = x.s.OwedByMemberId,
                CreditorId = x.p.PaidByMemberId
            })
            .Select(g => new DebtRow
            {
                DebtorId = g.Key.DebtorId,
                CreditorId = g.Key.CreditorId,
                Amount = g.Sum(x => x.s.Amount * x.p.Amount / x.TotalPaid)
            })
            .ToListAsync(ct);

        // Debts raw relationships between members
        // var debtsRaw = debtRows
        //     .Select(x =>
        //     {
        //         var amount = Math.Round(x.Amount, 2, MidpointRounding.AwayFromZero);

        //         return new DebtEdge
        //         {
        //             DebtorMemberId = x.DebtorId,
        //             DebtorDisplayName = names.TryGetValue(x.DebtorId, out var dn) ? dn : "Unknown",
        //             CreditorMemberId = x.CreditorId,
        //             CreditorDisplayName = names.TryGetValue(x.CreditorId, out var cn) ? cn : "Unknown",
        //             Amount = amount
        //         };
        //     })
        //     .Where(x => x.Amount != 0)
        //     .OrderByDescending(x => x.Amount)
        //     .ToList();

        // Debts relationships between members NORMALIZED
        var netByPair = BuildNetByPair(debtRows);

        var debtsNet = new List<DebtEdge>();

        debtsNet.AddRange(BuildNetDebts(netByPair, names));

        debtsNet = [.. debtsNet.OrderByDescending(x => x.Amount)];

        return new GroupBalances
        {
            GroupId = groupId,
            Members = members,
            Debts = debtsNet
        };
    }

    public async Task<(decimal paid, decimal owed, decimal net)>
    GetMemberNetInGroupAsync(
        Guid groupId,
        Guid memberId
    )
    {
        var paid = await db.ExpensePayments
            .AsNoTracking()
            .Where(x =>
                x.PaidByMemberId == memberId &&
                x.Expense.GroupId == groupId &&
                x.Expense.DeletedAt == null
            )
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        var owed = await db.ExpenseSplits
            .AsNoTracking()
            .Where(x =>
                x.OwedByMemberId == memberId &&
                x.Expense.GroupId == groupId &&
                x.Expense.DeletedAt == null
            )
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        paid = Math.Round(paid, 2, MidpointRounding.AwayFromZero);
        owed = Math.Round(owed, 2, MidpointRounding.AwayFromZero);

        var net = paid - owed;

        return (paid, owed, net);
    }

    private static List<DebtEdge> BuildNetDebts(
        Dictionary<(Guid A, Guid B), decimal> netByPair,
        Dictionary<Guid, string> names
    )
    {
        const decimal noiseThreshold = 0.00005m;
        var debtsNet = new List<DebtEdge>(netByPair.Count);

        foreach (var kvp in netByPair)
        {
            var a = kvp.Key.A;
            var b = kvp.Key.B;
            var net = kvp.Value;

            if (net > -noiseThreshold && net < noiseThreshold)
                continue;

            Guid debtorId;
            Guid creditorId;
            decimal amount;

            if (net > 0)
            {
                debtorId = a;
                creditorId = b;
                amount = net;
            }
            else
            {
                debtorId = b;
                creditorId = a;
                amount = -net;
            }

            amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
            if (amount == 0)
                continue;

            debtsNet.Add(new DebtEdge
            {
                DebtorMemberId = debtorId,
                DebtorDisplayName = names.TryGetValue(debtorId, out var dn) ? dn : "Unknown",
                CreditorMemberId = creditorId,
                CreditorDisplayName = names.TryGetValue(creditorId, out var cn) ? cn : "Unknown",
                Amount = amount
            });
        }

        return debtsNet;
    }

    private static Dictionary<(Guid A, Guid B), decimal> BuildNetByPair(IReadOnlyList<DebtRow> debtRows)
    {
        var netByPair = new Dictionary<(Guid A, Guid B), decimal>(debtRows.Count);

        foreach (var r in debtRows)
        {
            var debtor = r.DebtorId;
            var creditor = r.CreditorId;

            var cmp = debtor.CompareTo(creditor);
            if (cmp == 0) continue;

            var a = cmp < 0 ? debtor : creditor;
            var b = cmp < 0 ? creditor : debtor;

            // if debtor is A => +amount (A owes B), else -amount (B owes A)
            var signed = cmp < 0 ? r.Amount : -r.Amount;

            netByPair.TryGetValue((a, b), out var current);
            netByPair[(a, b)] = current + signed;
        }

        return netByPair;
    }

    private sealed class DebtRow
    {
        public Guid DebtorId { get; set; }
        public Guid CreditorId { get; set; }
        public decimal Amount { get; set; }
    }
}
