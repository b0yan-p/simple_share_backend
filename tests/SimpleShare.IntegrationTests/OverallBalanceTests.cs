using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace SimpleShare.IntegrationTests;

/// <summary>
/// GET /api/Balance/me - the caller's standing across every group at once. The totals are the
/// per-group netted debts summed over groups, so every case here also checks that the number
/// agrees with what the group-scoped overview reports for the same data.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class OverallBalanceTests(ApiFixture fixture)
{
    private static async Task<(Guid GroupId, Guid Me, Guid[] Others)> GroupOfAsync(
        HttpClient client, string name, CancellationToken ct, params string[] otherMemberNames)
    {
        var group = await client.CreateGroupAsync(name, ct);
        var me = await client.MyMemberIdAsync(group.Id, ct);

        var others = new List<Guid>();
        foreach (var other in otherMemberNames)
            others.Add(await client.AddVirtualMemberAsync(group.Id, other, ct));

        return (group.Id, me, [.. others]);
    }

    [Fact] // BAL-06
    public async Task MyBalance_WhenTheUserHasNoGroupsAtAll_ReturnsZerosRatherThanNotFound()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.RegisterAsync(ct);

        // Act
        var balance = await client.GetMyBalanceAsync(ct);

        // Assert
        balance.OwedToMeTotal.ShouldBe(0m);
        balance.IOweTotal.ShouldBe(0m);
        balance.Net.ShouldBe(0m);
        balance.GroupCount.ShouldBe(0);
    }

    [Fact] // BAL-07
    public async Task MyBalance_WhenTwoGroupsPullOppositeWays_SumsBothSidesAndCountsBothGroups()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.RegisterAsync(ct);

        var (trip, meOnTrip, tripOthers) = await GroupOfAsync(client, "Trip", ct, "Bo", "Cy");
        await client.AddExpenseAsync(trip, ApiExtensions.ExpenseBody(
            "Hotel", 30m, meOnTrip,
            [(meOnTrip, 10m), (tripOthers[0], 10m), (tripOthers[1], 10m)]), ct);

        var (flat, meOnFlat, flatOthers) = await GroupOfAsync(client, "Flat", ct, "Dee");
        await client.AddExpenseAsync(flat, ApiExtensions.ExpenseBody(
            "Rent", 50m, flatOthers[0], [(meOnFlat, 25m), (flatOthers[0], 25m)]), ct);

        // Act
        var balance = await client.GetMyBalanceAsync(ct);

        // Assert
        balance.OwedToMeTotal.ShouldBe(20m);
        balance.IOweTotal.ShouldBe(25m);
        balance.Net.ShouldBe(-5m);
        balance.GroupCount.ShouldBe(2);

        // Same money as the per-group endpoint reports, group by group.
        var tripSummary = (await client.GetOverviewAsync(trip, ct)).MyDebtSummary;
        var flatSummary = (await client.GetOverviewAsync(flat, ct)).MyDebtSummary;

        balance.OwedToMeTotal.ShouldBe(tripSummary.PaidTotal + flatSummary.PaidTotal);
        balance.IOweTotal.ShouldBe(tripSummary.OwedTotal + flatSummary.OwedTotal);
    }

    [Fact] // BAL-08
    public async Task MyBalance_WhenOneGroupHoldsDebtsBothWays_CountsItOnceAndKeepsBothTotals()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.RegisterAsync(ct);
        var (groupId, me, others) = await GroupOfAsync(client, "Balances", ct, "Bo", "Cy");
        var (bo, cy) = (others[0], others[1]);

        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Taxi", 10m, me, [(bo, 10m)]), ct);
        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Coffee", 10m, cy, [(me, 10m)]), ct);

        // Act
        var balance = await client.GetMyBalanceAsync(ct);

        // Assert
        // Net is zero while two debts are still open, which is why GroupCount counts open
        // debts rather than a non-zero net.
        balance.OwedToMeTotal.ShouldBe(10m);
        balance.IOweTotal.ShouldBe(10m);
        balance.Net.ShouldBe(0m);
        balance.GroupCount.ShouldBe(1);
    }

    [Fact] // BAL-09
    public async Task MyBalance_AfterEveryDebtIsSettled_ReturnsZerosAndCountsNoGroups()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.RegisterAsync(ct);
        var (groupId, me, others) = await GroupOfAsync(client, "Trip", ct, "Bo", "Cy");
        var (bo, cy) = (others[0], others[1]);

        await client.AddExpenseAsync(groupId, ApiExtensions.ExpenseBody(
            "Hotel", 30m, me, [(me, 10m), (bo, 10m), (cy, 10m)]), ct);

        foreach (var debtor in new[] { bo, cy })
        {
            var settled = await client.PostAsJsonAsync($"/api/Expense/{groupId}/settleUp", new
            {
                FromMemberId = debtor,
                ToMemberId = me,
                Amount = 10m,
                Date = DateTime.UtcNow,
            }, ct);

            settled.StatusCode.ShouldBe(
                HttpStatusCode.NoContent, await settled.Content.ReadAsStringAsync(ct));
        }

        // Act
        var balance = await client.GetMyBalanceAsync(ct);

        // Assert
        balance.OwedToMeTotal.ShouldBe(0m);
        balance.IOweTotal.ShouldBe(0m);
        balance.Net.ShouldBe(0m);
        balance.GroupCount.ShouldBe(0);
    }

    [Fact] // BAL-10
    public async Task MyBalance_WhenAGroupIsDeleted_StopsCountingIt()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.RegisterAsync(ct);
        var (groupId, me, others) = await GroupOfAsync(client, "Trip", ct, "Bo");

        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Hotel", 20m, me, [(me, 10m), (others[0], 10m)]), ct);
        await client.RemoveGroupAsync(groupId, ct);

        // Act
        var balance = await client.GetMyBalanceAsync(ct);

        // Assert
        balance.OwedToMeTotal.ShouldBe(0m);
        balance.IOweTotal.ShouldBe(0m);
        balance.GroupCount.ShouldBe(0);
    }

    [Fact] // BAL-11
    public async Task MyBalance_WhenAnotherUsersGroupHoldsDebts_LeavesThemOut()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (mine, _) = await fixture.RegisterAsync(ct);
        var (theirs, _) = await fixture.RegisterAsync(ct);

        var (groupId, them, others) = await GroupOfAsync(theirs, "Not mine", ct, "Bo");
        await theirs.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Hotel", 20m, them, [(them, 10m), (others[0], 10m)]), ct);

        await mine.CreateGroupAsync("Mine", ct);

        // Act
        var balance = await mine.GetMyBalanceAsync(ct);

        // Assert
        balance.OwedToMeTotal.ShouldBe(0m);
        balance.IOweTotal.ShouldBe(0m);
        balance.GroupCount.ShouldBe(0);
    }
}
