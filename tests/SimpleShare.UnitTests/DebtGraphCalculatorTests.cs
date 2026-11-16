using Xunit;
using static simpleShare_backend.Services.Calculations.DebtGraphCalculator;

public class DebtGraphCalculatorTests
{
    static Guid Id(int n) => new(n, 0, 0, new byte[8]);
    static Result Run(params (int D, int C, decimal A)[] edges)
    {
        var members = edges.SelectMany(x => new[] { x.D, x.C }).Distinct().Select(i => new Member(Id(i), edges.Where(x => x.C == i).Sum(x => x.A), edges.Where(x => x.D == i).Sum(x => x.A))).ToArray();
        var raw = edges.Select(x => new RawDebt(Id(x.D), Id(x.C), x.A)).ToArray();
        var result = Calculate(members, raw);
        Assert.Equal(0, result.Members.Sum(x => x.Net));
        foreach (var graph in new[] { result.Regular, result.Simplified })
        {
            foreach (var m in result.Members) Assert.Equal(m.Net, graph.Where(x => x.Creditor == m.Id).Sum(x => x.Cents) - graph.Where(x => x.Debtor == m.Id).Sum(x => x.Cents));
            Assert.All(graph, x => Assert.True(x.Cents > 0 && x.Debtor != x.Creditor));
            var remaining = graph.ToList();
            while (remaining.Count > 0) { var sources = remaining.Select(x => x.Debtor).Except(remaining.Select(x => x.Creditor)).ToHashSet(); Assert.NotEmpty(sources); remaining.RemoveAll(x => sources.Contains(x.Debtor)); }
        }
        Assert.True(result.Simplified.Count <= result.Regular.Count);
        Assert.Equal(result.Regular, Calculate(members.Reverse(), raw.Reverse()).Regular);
        Assert.Equal(result.Simplified, Calculate(members.Reverse(), raw.Reverse()).Simplified);
        return result;
    }
    [Fact] public void Chain() { var r = Run((1,2,10),(2,3,10)); Assert.Equal(2,r.Regular.Count); Assert.Equal(new Debt(Id(1),Id(3),1000),Assert.Single(r.Simplified)); }

    private static readonly (int D, int C, decimal A)[] RequestedGraph =
        [(1, 2, 8.67m), (3, 2, .66m), (3, 1, 10m), (4, 1, 25m), (4, 3, 15m)];

    [Fact]
    public void RequestedGraphProducesExactThreeTransfers()
    {
        var result = Run(RequestedGraph);
        Assert.Equal(5, result.Regular.Count);
        Assert.Equal(new[] { new Debt(Id(4), Id(1), 2633), new Debt(Id(4), Id(2), 933), new Debt(Id(4), Id(3), 434) }, result.Simplified);
        Assert.Equal(2633, result.Members.Single(x => x.Id == Id(1)).Net);
        Assert.Equal(933, result.Members.Single(x => x.Id == Id(2)).Net);
        Assert.Equal(434, result.Members.Single(x => x.Id == Id(3)).Net);
        Assert.Equal(-4000, result.Members.Single(x => x.Id == Id(4)).Net);
    }

    [Fact]
    public void RequestedGraphFullySettlesAndDeletingOnePaymentReopensOnlyItsNet()
    {
        var full = Run(RequestedGraph.Concat([(1, 4, 26.33m), (2, 4, 9.33m), (3, 4, 4.34m)]).ToArray());
        Assert.Empty(full.Regular);
        Assert.Empty(full.Simplified);
        var deleted = Run(RequestedGraph.Concat([(1, 4, 26.33m), (3, 4, 4.34m)]).ToArray());
        Assert.Equal(new Debt(Id(4), Id(2), 933), Assert.Single(deleted.Simplified));
    }

    [Fact]
    public void StaleAmountRecordsRealOverpaymentWithoutResurrectingOriginalDebts()
    {
        var result = Run(RequestedGraph.Concat([(1, 4, 10m), (1, 4, 26.33m)]).ToArray());
        Assert.Equal(-1000, result.Members.Single(x => x.Id == Id(1)).Net);
        Assert.Equal(-367, result.Members.Single(x => x.Id == Id(4)).Net);
    }

    [Fact]
    public void StaleLinkAfterSettlementCreatesOnlyTheNewOppositeDebt()
    {
        var result = Run((1, 2, 10), (2, 3, 10), (3, 1, 10), (3, 1, 10));
        Assert.Equal(new Debt(Id(3), Id(1), 1000), Assert.Single(result.Regular));
        Assert.Equal(result.Regular, result.Simplified);
    }
    [Theory] [InlineData(0,2,1000)] [InlineData(4,2,600)] [InlineData(10,0,0)] [InlineData(12,1,200)]
    public void Settlement(decimal paid,int count,long amount) { var r=Run((1,2,10),(2,3,10),(3,1,paid)); Assert.Equal(count,r.Regular.Count); if(amount>0) Assert.All(r.Regular,x=>Assert.Equal(amount,x.Cents)); }
    [Fact] public void Disconnected() { var r=Run((1,2,3),(2,3,7),(4,5,5)); Assert.Equal(3,r.Simplified.Count); Assert.Contains(new Debt(Id(4),Id(5),500),r.Simplified); }
    [Theory] [InlineData("0.004",0)] [InlineData("0.005",1)] [InlineData("0.009",1)] [InlineData("0.0051",1)] [InlineData("0.0049",0)]
    public void Fractions(string amount,long expected) { var r=Run((2,1,decimal.Parse(amount,System.Globalization.CultureInfo.InvariantCulture))); Assert.Equal(expected,r.Members.Single(x=>x.Id==Id(1)).Net); }
    [Theory] [InlineData("0.005",1,0)] [InlineData("5.0025",501,500)] [InlineData("0.0075",1,1)]
    public void Remainders(string amount,long b,long c) { var a=decimal.Parse(amount,System.Globalization.CultureInfo.InvariantCulture); var r=Run((2,1,a),(3,1,a)); Assert.Equal(-b,r.Members.Single(x=>x.Id==Id(2)).Net); Assert.Equal(-c,r.Members.Single(x=>x.Id==Id(3)).Net); }
    [Fact] public void Empty() { Assert.Empty(Calculate([],[]).Regular); }
    [Fact] public void InvalidTotals() { Assert.Throws<ArgumentException>(()=>Calculate([new(Id(1),1,0)],[])); }
    [Fact] public void InvalidLedger() { Assert.Throws<ArgumentException>(()=>Calculate([new(Id(1),1,0),new(Id(2),0,1)],[])); }
    [Fact] public void Negative() { Assert.Throws<ArgumentException>(()=>Calculate([new(Id(1),-1,-1)],[])); }
    [Fact] public void GeneratedGraphs() { var random=new Random(42); for(int i=0;i<100;i++) Run(Enumerable.Range(0,20).Select(_=>(random.Next(1,8),random.Next(1,8),random.Next(1,10000)/10000m)).ToArray()); }
    [Fact] public void MultiplePayers() { Run((3,1,.165m),(3,2,.165m),(4,1,.165m),(4,2,.165m),(5,1,.17m),(5,2,.17m)); }
    [Fact] public void OverlappingCycles() { Run((1,2,10),(2,3,7),(3,1,4),(2,4,3),(4,1,2)); }
    [Theory] [InlineData("0.005")] [InlineData("5.0025")] [InlineData("0.0075")]
    public void FractionalDebtsRemainSettled(string amount)
    {
        var a = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        var original = new[] { (D: 2, C: 1, A: a), (D: 3, C: 1, A: a) };
        var result = Run(original);
        var settlement = result.Simplified.Select(e => (D: e.Creditor == Id(1) ? 1 : 0,
            C: e.Debtor == Id(2) ? 2 : 3, A: e.Cents / 100m));
        var settled = Run(original.Concat(settlement).ToArray());
        Assert.Empty(settled.Regular);
        Assert.Empty(settled.Simplified);
        Assert.All(settled.Members, m => Assert.Equal(0, m.Net));
    }
    [Fact] public void UnequalCycle() { var r = Run((1,2,10),(2,3,7),(3,1,4)); Assert.Equal(new[] { new Debt(Id(1),Id(2),600),new Debt(Id(2),Id(3),300) },r.Regular); }
    [Fact] public void PartialPaymentThenRegularPayment() { var r=Run((1,2,10),(2,3,10),(3,1,4),(2,1,3)); Assert.Equal(new[] { new Debt(Id(2),Id(3),600),new Debt(Id(1),Id(2),300) },r.Regular); }
    [Fact] public void IsolatedSelfExpense() { var r=Calculate([new(Id(1),.005m,.005m)],[]); Assert.Equal(1,r.Members[0].Paid); Assert.Equal(1,r.Members[0].Owed); Assert.Empty(r.Regular); }
    [Fact] public void UnknownMember() { Assert.Throws<ArgumentException>(()=>Calculate([], [new(Id(1),Id(2),1)])); }
    [Fact] public void DuplicateMember() { Assert.Throws<ArgumentException>(()=>Calculate([new(Id(1),0,0),new(Id(1),0,0)], [])); }
    [Fact] public void NegativeDebt() { Assert.Throws<ArgumentException>(()=>Calculate([new(Id(1),0,0)], [new(Id(1),Id(1),-1)])); }
    [Fact] public void SubCentDiscrepancyIsRejected() { Assert.Throws<ArgumentException>(()=>Calculate([new(Id(1),.0001m,0)], [])); }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroDebtDoesNotJoinOrBreakIndependentComponents(bool reverse)
    {
        var members = new[] { new Member(Id(1), 0, 1), new Member(Id(2), 1, 0),
            new Member(Id(3), 0, 2), new Member(Id(4), 2, 0), new Member(Id(5), 0, 0) };
        var debts = new[] { new RawDebt(Id(1), Id(2), 1), new RawDebt(Id(3), Id(4), 2) };
        var baseline = Calculate(members, debts);
        var zero = reverse ? new RawDebt(Id(3), Id(1), 0) : new RawDebt(Id(1), Id(3), 0);
        var result = Calculate(members, debts.Append(zero).Append(new RawDebt(Id(5), Id(2), 0)));
        Assert.Equal(baseline.Members, result.Members);
        Assert.Equal(baseline.Regular, result.Regular);
        Assert.Equal(baseline.Simplified, result.Simplified);
    }

    [Fact]
    public void ZeroDebtStillRequiresKnownMembers()
    {
        Assert.Throws<ArgumentException>(() => Calculate([new(Id(1), 0, 0)], [new(Id(1), Id(2), 0)]));
    }
}
