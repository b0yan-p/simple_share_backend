using Xunit;
using simpleShare_backend.Contracts.Group;
using simpleShare_backend.Services;
using simpleShare_backend.Services.Calculations;
using System.Text.Json;

public class BalanceLedgerTests
{
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(31)]
    public void RepeatingProportionsKeepExactSourceTotals(int count)
    {
        var payments = Enumerable.Range(1, count).Select(i => (Id(i), 1m)).ToArray();
        var shares = new[] { (Id(100), .0001m), (Id(101), .0001m), (Id(102), count - .0002m) };
        var raw = BalanceService.BuildExpenseLedger(count, payments, shares);
        foreach (var (id, amount) in payments) Assert.Equal(amount, raw.Where(x => x.Creditor == id).Sum(x => x.Amount));
        foreach (var (id, amount) in shares) Assert.Equal(amount, raw.Where(x => x.Debtor == id).Sum(x => x.Amount));
        Assert.All(raw, x => Assert.True(x.Amount >= 0));
        var members = payments.Select(x => new DebtGraphCalculator.Member(x.Item1, x.Item2, 0))
            .Concat(shares.Select(x => new DebtGraphCalculator.Member(x.Item1, 0, x.Item2)));
        var calculated = DebtGraphCalculator.Calculate(members, raw);
        Assert.Equal(0, calculated.Members.Sum(x => x.Net));
        Assert.Equal(raw, BalanceService.BuildExpenseLedger(count, payments.Reverse().ToArray(), shares.Reverse().ToArray()));
    }

    [Theory]
    [InlineData("0.0001")]
    [InlineData("0.01")]
    public void SourceMismatchIsRejectedBeforeAllocation(string discrepancy)
    {
        var difference = decimal.Parse(discrepancy, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentException>(() => BalanceService.BuildExpenseLedger(1,
            [(Id(1), 1)], [(Id(2), 1 - difference)]));
    }

    [Fact]
    public void SelfPaymentsPreserveGrossTotalsAndNet()
    {
        var raw = BalanceService.BuildExpenseLedger(1, [(Id(1), 1)], [(Id(1), 1)]);
        var result = DebtGraphCalculator.Calculate([new(Id(1), 1, 1)], raw);
        Assert.Equal(100, result.Members[0].Paid);
        Assert.Equal(100, result.Members[0].Owed);
        Assert.Empty(result.Regular);
    }

    [Fact]
    public void GeneratedAllocationsPreserveMarginalsIncludingSelfShares()
    {
        var random = new Random(71);
        for (var trial = 0; trial < 100; trial++)
        {
            var payments = Enumerable.Range(1, 5).Select(i => (Id: Id(i), Amount: random.Next(1, 10000) / 10000m)).ToArray();
            var total = payments.Sum(x => x.Amount);
            var third = decimal.Floor(total / 3 * 10000) / 10000;
            var shares = new[] { (Id: Id(1), Amount: third), (Id: Id(20), Amount: third), (Id: Id(21), Amount: total - 2 * third) };
            var raw = BalanceService.BuildExpenseLedger(total, payments, shares);
            foreach (var p in payments) Assert.Equal(p.Amount, raw.Where(x => x.Creditor == p.Id).Sum(x => x.Amount));
            foreach (var s in shares) Assert.Equal(s.Amount, raw.Where(x => x.Debtor == s.Id).Sum(x => x.Amount));
            var ids = payments.Select(x => x.Id).Concat(shares.Select(x => x.Id)).Distinct();
            var members = ids.Select(id => new DebtGraphCalculator.Member(id,
                payments.Where(x => x.Id == id).Sum(x => x.Amount), shares.Where(x => x.Id == id).Sum(x => x.Amount)));
            var calculated = DebtGraphCalculator.Calculate(members, raw);
            Assert.Equal(0, calculated.Members.Sum(x => x.Net));
            Assert.True(calculated.Simplified.Count <= calculated.Regular.Count);
        }
    }

    [Theory]
    [InlineData("{\"Name\":\"Renamed\"}", null)]
    [InlineData("{\"Name\":\"Renamed\",\"SimplifyDebts\":true}", true)]
    [InlineData("{\"Name\":\"Renamed\",\"SimplifyDebts\":false}", false)]
    public void SettingsDistinguishOmittedAndExplicitValues(string json, bool? expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<CreateGroupRequest>(json)!.SimplifyDebts);
    }

    [Fact]
    public void UnallocatedProportionalThirdsCannotRepresentExactMemberTotals()
    {
        // The old split * payment / total formula cannot express thirds exactly in decimal.
        var members = new[] { new DebtGraphCalculator.Member(Id(1), 1, 0), new DebtGraphCalculator.Member(Id(2), 2, 0),
            new DebtGraphCalculator.Member(Id(3), 0, 1), new DebtGraphCalculator.Member(Id(4), 0, 1),
            new DebtGraphCalculator.Member(Id(5), 0, 1) };
        var naive = (from debtor in members.Where(x => x.Owed > 0)
                     from payer in members.Where(x => x.Paid > 0)
                     select new DebtGraphCalculator.RawDebt(debtor.Id, payer.Id, debtor.Owed * payer.Paid / 3m)).ToArray();
        Assert.NotEqual(1m, naive.Where(x => x.Creditor == Id(1)).Sum(x => x.Amount));
        Assert.Throws<ArgumentException>(() => DebtGraphCalculator.Calculate(members, naive));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServiceAdapterAllocatesThirdsExactlyInBothModes(bool simplify)
    {
        var payments = new[] { (Id: Id(1), Amount: 1m), (Id: Id(2), Amount: 2m) };
        var splits = new[] { (Id: Id(3), Amount: 1m), (Id: Id(4), Amount: 1m), (Id: Id(5), Amount: 1m) };
        var raw = BalanceService.BuildExpenseLedger(3m, payments, splits);
        foreach (var p in payments) Assert.Equal(p.Amount, raw.Where(x => x.Creditor == p.Id).Sum(x => x.Amount));
        foreach (var s in splits) Assert.Equal(s.Amount, raw.Where(x => x.Debtor == s.Id).Sum(x => x.Amount));
        var members = payments.Select(x => new DebtGraphCalculator.Member(x.Id, x.Amount, 0))
            .Concat(splits.Select(x => new DebtGraphCalculator.Member(x.Id, 0, x.Amount)));
        var result = DebtGraphCalculator.Calculate(members, raw);
        var graph = simplify ? result.Simplified : result.Regular;
        Assert.Equal(0, result.Members.Sum(x => x.Net));
        Assert.Equal(100, result.Members.Single(x => x.Id == Id(1)).Net);
        Assert.Equal(200, result.Members.Single(x => x.Id == Id(2)).Net);
        foreach (var member in result.Members)
            Assert.Equal(member.Net, graph.Where(x => x.Creditor == member.Id).Sum(x => x.Cents)
                - graph.Where(x => x.Debtor == member.Id).Sum(x => x.Cents));
        Assert.All(graph, x => Assert.True(x.Cents > 0));
        Assert.Equal(300, result.Members.Where(x => x.Net > 0).Sum(x => x.Net));
        // Regular graphs can retain an intermediary from cent reconciliation;
        // edge volume is not the same invariant as the sum of positive net balances.
        if (simplify) Assert.Equal(300, graph.Sum(x => x.Cents));
        // A real source mismatch is rejected even though both sides round to 3.00.
        Assert.Throws<ArgumentException>(() => BalanceService.BuildExpenseLedger(3m, payments,
            [(Id(3), 1m), (Id(4), 1m), (Id(5), .9999m)]));
    }
}
