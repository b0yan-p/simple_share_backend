using System.Net;
using System.Net.Http.Json;
using Shouldly;
using simpleShare_backend.Models.Enums;
using simpleShare_backend.Contracts.Group;

namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class GroupTests(ApiFixture fixture)
{
    [Fact] // GRP-01
    public async Task CreateGroup_WithValidName_ReturnsGroupWithId()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = await fixture.CreateClientWithoutMemberAsync(ct);

        // Act
        var response = await client.PostAsJsonAsync("/api/Group", new { Name = "Trip", Currency = "BAM" }, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var group = await response.Content.ReadFromJsonAsync<GroupResponse>(ct);
        group.ShouldNotBeNull();
        group.Id.ShouldNotBe(Guid.Empty);
        group.Name.ShouldBe("Trip");
        group.Type.ShouldBe(GroupType.Other);
        group.SimplifyDebts.ShouldBeFalse();
        group.DeletedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(GroupType.Other)]
    [InlineData(GroupType.Trip)]
    [InlineData(GroupType.Roommates)]
    [InlineData(GroupType.Couple)]
    public async Task GroupTypeRoundTripsAcrossGroupEndpoints(GroupType type)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var created = await client.PostAsJsonAsync("/api/Group", new { Name = "Shared", Currency = "EUR", Type = type }, ct);
        created.EnsureSuccessStatusCode();
        var group = (await created.Content.ReadFromJsonAsync<GroupResponse>(ct))!;
        group.Type.ShouldBe(type);

        var renamed = await client.PutAsJsonAsync("/api/Group", new { group.Id, Name = "Renamed" }, ct);
        renamed.EnsureSuccessStatusCode();
        (await client.GetMyGroupsAsync(ct)).Data.Single().Type.ShouldBe(type);
        var recent = await client.GetFromJsonAsync<List<GroupResponse>>("/api/Group/recent", ct);
        recent!.Single().Type.ShouldBe(type);
        var overview = await client.GetFromJsonAsync<GroupOverviewResponse>($"/api/Group/{group.Id}/overview", ct);
        overview!.Type.ShouldBe(type);

        var changedType = type == GroupType.Trip ? GroupType.Other : GroupType.Trip;
        var updated = await client.PutAsJsonAsync("/api/Group", new { group.Id, Type = changedType }, ct);
        updated.EnsureSuccessStatusCode();
        var saved = await client.GetFromJsonAsync<GroupResponse>($"/api/Group/{group.Id}", ct);
        saved!.Type.ShouldBe(changedType);
    }

    [Fact]
    public async Task InvalidGroupTypeIsRejectedOnCreateAndUpdate()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var invalid = await client.PostAsJsonAsync("/api/Group", new { Name = "Shared", Currency = "EUR", Type = 99 }, ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var group = await client.CreateGroupAsync("Shared", ct);
        invalid = await client.PutAsJsonAsync("/api/Group", new { group.Id, Type = -1 }, ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact] // GRP-02
    public async Task UpdateGroup_WithNewName_PersistsNameOnReadBack()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Before", ct);

        // Act
        var response = await client.PutAsJsonAsync(
            "/api/Group", new { Id = group.Id, Name = "After" }, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var readBack = await client.GetFromJsonAsync<GroupResponse>($"/api/Group/{group.Id}", ct);
        readBack.ShouldNotBeNull();
        readBack.Id.ShouldBe(group.Id);
        readBack.Name.ShouldBe("After");
    }

    [Fact] // GRP-03
    public async Task GetMyGroups_ListsOnlyCallersGroups_AndRespectsPaging()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        await client.CreateGroupAsync("Mine 1", ct);
        await client.CreateGroupAsync("Mine 2", ct);
        await client.CreateGroupAsync("Mine 3", ct);

        var (stranger, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var strangerGroup = await stranger.CreateGroupAsync("Not mine", ct);

        // Act
        var firstPage = await client.GetMyGroupsAsync(ct, skip: 0, take: 2);

        // Assert
        firstPage.TotalCount.ShouldBe(3);
        firstPage.Data.Count.ShouldBe(2);

        var everything = await client.GetMyGroupsAsync(ct);
        everything.Data.Select(g => g.Name).ShouldBe(["Mine 3", "Mine 2", "Mine 1"], ignoreOrder: true);
        everything.Data.ShouldNotContain(g => g.Id == strangerGroup.Id);
    }

    [Fact] // GRP-04
    public async Task GetRecentGroups_WithTakeTwo_ReturnsTwoMostRecentFirst()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        await client.CreateGroupAsync("Oldest", ct);
        var middle = await client.CreateGroupAsync("Middle", ct);
        var newest = await client.CreateGroupAsync("Newest", ct);

        // Act
        var recent = await client.GetFromJsonAsync<List<GroupResponse>>("/api/Group/recent?take=2", ct);

        // Assert
        recent.ShouldNotBeNull();
        recent.Count.ShouldBe(2);
        recent.Select(g => g.Id).ShouldBe([newest.Id, middle.Id]);
    }

    [Fact] // GRP-05
    public async Task GetGroupById_ForOwnGroup_ReturnsOk()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Flat", ct);

        // Act
        var response = await client.GetAsync($"/api/Group/{group.Id}", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var body = await response.Content.ReadFromJsonAsync<GroupResponse>(ct);
        body.ShouldNotBeNull();
        body.Id.ShouldBe(group.Id);
        body.Name.ShouldBe("Flat");
    }

    [Fact] // GRP-06
    public async Task DeleteGroup_SoftDeletes_RemovesItFromMyGroups()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var doomed = await client.CreateGroupAsync("Doomed", ct);
        var kept = await client.CreateGroupAsync("Kept", ct);

        // Act
        var response = await client.DeleteAsync($"/api/Group/{doomed.Id}", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var mine = await client.GetMyGroupsAsync(ct);
        mine.Data.ShouldNotContain(g => g.Id == doomed.Id);
        mine.Data.ShouldContain(g => g.Id == kept.Id);
    }

    [Fact] // GRP-07
    public async Task RestoreGroup_AfterDelete_ReturnsGroupWithItsExpenses()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Resurrected", ct);
        var me = await client.MyMemberIdAsync(group.Id, ct);
        var flatmate = await client.AddVirtualMemberAsync(group.Id, "Flatmate", ct);
        await client.AddExpenseAsync(
            group.Id,
            ApiExtensions.ExpenseBody("Rent", 10m, me, [(me, 5m), (flatmate, 5m)]),
            ct);
        await client.RemoveGroupAsync(group.Id, ct);

        // Act
        var response = await client.PostAsync($"/api/Group/{group.Id}/restore", null, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var mine = await client.GetMyGroupsAsync(ct);
        mine.Data.ShouldContain(g => g.Id == group.Id);

        var expenses = await client.GetExpensesAsync(group.Id, ct);
        expenses.TotalCount.ShouldBe(1);
        expenses.Data.ShouldHaveSingleItem().Description.ShouldBe("Rent");
    }

    [Fact] // GRP-08
    public async Task GetGroupOverview_AfterTwoExpenses_TotalsMatchTheExpenses()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Holiday", ct);
        var me = await client.MyMemberIdAsync(group.Id, ct);
        var bo = await client.AddVirtualMemberAsync(group.Id, "Bo", ct);
        var cy = await client.AddVirtualMemberAsync(group.Id, "Cy", ct);

        // I pay for both: 30 split three ways, then 15 split three ways.
        await client.AddExpenseAsync(
            group.Id,
            ApiExtensions.ExpenseBody("Hotel", 30m, me, [(me, 10m), (bo, 10m), (cy, 10m)]),
            ct);
        await client.AddExpenseAsync(
            group.Id,
            ApiExtensions.ExpenseBody("Fuel", 15m, me, [(me, 5m), (bo, 5m), (cy, 5m)]),
            ct);

        // Act
        var response = await client.GetAsync($"/api/Group/{group.Id}/overview", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var overview = await response.Content.ReadFromJsonAsync<GroupOverviewResponse>(ct);
        overview.ShouldNotBeNull();
        overview.Id.ShouldBe(group.Id);
        overview.MemberCount.ShouldBe(3);
        overview.CurrentMemberId.ShouldBe(me);

        // I put in 45 and consumed 15, so the other two owe me 15 each.
        overview.MyDebtSummary.PaidTotal.ShouldBe(30m);
        overview.MyDebtSummary.OwedTotal.ShouldBe(0m);
        overview.MyDebtSummary.DebtEdges.Count.ShouldBe(2);
        overview.MyDebtSummary.DebtEdges.ShouldAllBe(e => e.SignedAmount == 15m);
    }

    [Fact] // GRP-09
    public async Task CreateGroup_WithNameOverMaxLength_ReturnsValidationError()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var request = new { Name = new string('x', 49), Currency = "BAM" };   // MaxLength(48)

        // Act
        var response = await client.PostAsJsonAsync("/api/Group", request, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_error", ct);
    }
}
