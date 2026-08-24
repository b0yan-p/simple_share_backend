using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace SimpleShare.IntegrationTests;

/// <summary>
/// Balances are the only place a rounding mistake turns into money, so every case
/// here ends on BAL-02: the members' nets must cancel to exactly zero.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class BalanceTests(ApiFixture fixture)
{
    private async Task<(HttpClient Client, Guid GroupId, Guid[] Members)>
        GroupOfAsync(CancellationToken ct, params string[] otherMemberNames)
    {
        var (client, _) = await fixture.RegisterAsync(ct);
        var group = await client.CreateGroupAsync("Balances", ct);
        var me = await client.MyMemberIdAsync(group.Id, ct);

        var members = new List<Guid> { me };
        foreach (var name in otherMemberNames)
            members.Add(await client.AddVirtualMemberAsync(group.Id, name, ct));

        return (client, group.Id, [.. members]);
    }

    [Fact] // BAL-01
    public async Task Balances_WhenOnePersonPaysAnEvenThreeWaySplit_OwesThemEachAThird()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, members) = await GroupOfAsync(ct, "Bo", "Cy");
        var (me, bo, cy) = (members[0], members[1], members[2]);
        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Hotel", 30m, me, [(me, 10m), (bo, 10m), (cy, 10m)]), ct);

        // Act
        var balances = await client.GetBalancesAsync(groupId, ct);

        // Assert
        balances.GroupId.ShouldBe(groupId);
        balances.CurrentMemberId.ShouldBe(me);

        balances.NetOf(me).ShouldBe(20m);
        balances.NetOf(bo).ShouldBe(-10m);
        balances.NetOf(cy).ShouldBe(-10m);

        balances.ShouldOwe(debtor: bo, creditor: me, amount: 10m);
        balances.ShouldOwe(debtor: cy, creditor: me, amount: 10m);
        balances.Debts.Count.ShouldBe(2);
        balances.ShouldNetToZero();
    }

    [Fact] // BAL-02
    public async Task Balances_AfterCreatesUpdatesDeletesAndASettleUp_StillNetToZero()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, members) = await GroupOfAsync(ct, "Bo", "Cy");
        var (me, bo, cy) = (members[0], members[1], members[2]);

        var hotel = await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Hotel", 30m, me, [(me, 10m), (bo, 10m), (cy, 10m)]), ct);
        var lunch = await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Lunch", 12m, bo, [(me, 4m), (bo, 4m), (cy, 4m)]), ct);

        await client.UpdateExpenseAsync(groupId, new
        {
            Id = hotel.Id,
            TotalAmount = 60m,
            Payments = new[] { new { MemberId = me, Amount = 60m } },
            Splits = new[]
            {
                new { MemberId = me, Amount = 20m },
                new { MemberId = bo, Amount = 20m },
                new { MemberId = cy, Amount = 20m },
            },
        }, ct);

        await client.RemoveExpenseAsync(groupId, lunch.Id, ct);

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}/settleUp", new
        {
            FromMemberId = cy,
            ToMemberId = me,
            Amount = 20m,
            Date = DateTime.UtcNow,
        }, ct);

        // Assert
        response.StatusCode.ShouldBe(
            HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(ct));

        var balances = await client.GetBalancesAsync(groupId, ct);

        balances.NetOf(me).ShouldBe(20m);
        balances.NetOf(bo).ShouldBe(-20m);
        balances.NetOf(cy).ShouldBe(0m);

        balances.ShouldOwe(debtor: bo, creditor: me, amount: 20m);
        balances.DebtBetween(debtor: cy, creditor: me).ShouldBeNull("Cy paid their share back");
        balances.ShouldNetToZero();
    }

    [Fact] // BAL-03
    public async Task Balances_WhenTenSplitsThreeWays_NeitherCreatesNorLosesACent()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, members) = await GroupOfAsync(ct, "Bo", "Cy");
        var (me, bo, cy) = (members[0], members[1], members[2]);

        // 10 / 3 does not divide, so one member carries the extra cent.
        var splits = ApiExtensions.EvenSplit(10m, me, bo, cy);
        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Coffee", 10m, me, splits), ct);

        // Act
        var balances = await client.GetBalancesAsync(groupId, ct);

        // Assert
        splits.Sum(s => s.Amount).ShouldBe(10m, "the split itself must not lose the cent");

        balances.NetOf(me).ShouldBe(6.66m);
        balances.NetOf(bo).ShouldBe(-3.33m);
        balances.NetOf(cy).ShouldBe(-3.33m);

        balances.ShouldOwe(debtor: bo, creditor: me, amount: 3.33m);
        balances.ShouldOwe(debtor: cy, creditor: me, amount: 3.33m);

        // What the debts ledger says is owed to me must be exactly what I am up.
        balances.Debts.Where(d => d.CreditorMemberId == me).Sum(d => d.Amount).ShouldBe(6.66m);
        balances.ShouldNetToZero();
    }

    [Fact] // BAL-04
    public async Task Balances_WithTwoExpensesInOppositeDirections_ReportOneNettedPair()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, members) = await GroupOfAsync(ct, "Bo");
        var (me, bo) = (members[0], members[1]);
        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Dinner", 20m, me, [(me, 10m), (bo, 10m)]), ct);
        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Cinema", 12m, bo, [(me, 6m), (bo, 6m)]), ct);

        // Act
        var balances = await client.GetBalancesAsync(groupId, ct);

        // Assert
        // One row for the pair, not one row per direction.
        balances.Debts.Count.ShouldBe(1);
        balances.ShouldOwe(debtor: bo, creditor: me, amount: 4m);

        balances.NetOf(me).ShouldBe(4m);
        balances.NetOf(bo).ShouldBe(-4m);
        balances.ShouldNetToZero();
    }

    [Fact] // BAL-05
    public async Task Balances_WhenAPairIsUnderTheSettleThreshold_ReportNoDebt()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, members) = await GroupOfAsync(ct, "Bo");
        var (me, bo) = (members[0], members[1]);

        // Amounts are numeric(19,4), so a client can land a pair half a cent
        // apart: Bo owes me 5.00 and I owe Bo 5.005.
        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Dinner", 10m, me, [(me, 5m), (bo, 5m)]), ct);
        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Cinema", 10.005m, bo, [(me, 5.005m), (bo, 5m)]), ct);

        // Act
        var balances = await client.GetBalancesAsync(groupId, ct);

        // Assert
        // 0.005 is under the 0.01 settle threshold, so the pair is settled rather
        // than carrying a debt rounded up to a whole cent.
        balances.Debts.ShouldBeEmpty(
            $"a half-cent gap must not become a debt. {string.Join(", ", balances.Debts.Select(d => d.Amount))}");
        balances.DebtBetween(debtor: me, creditor: bo).ShouldBeNull();
        balances.ShouldNetToZero();
    }
}
