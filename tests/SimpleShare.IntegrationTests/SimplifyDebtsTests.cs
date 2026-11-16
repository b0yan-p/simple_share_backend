using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using simpleShare_backend.Contracts.Group;
using simpleShare_backend.Data;

namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class SimplifyDebtsTests(ApiFixture fixture)
{
    private static async Task SetMode(HttpClient client, Guid group, bool value, CancellationToken ct)
    {
        var response = await client.PutAsJsonAsync("/api/Group", new { Id = group, Name = "Debts", SimplifyDebts = value }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultiplePayersWithThirdsUseExactBalancesAndStillRejectCorruption(bool simplify)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Thirds", ct);
        var a = await client.MyMemberIdAsync(group.Id, ct);
        var b = await client.AddVirtualMemberAsync(group.Id, "B", ct);
        var c = await client.AddVirtualMemberAsync(group.Id, "C", ct);
        var d = await client.AddVirtualMemberAsync(group.Id, "D", ct);
        var e = await client.AddVirtualMemberAsync(group.Id, "E", ct);
        await SetMode(client, group.Id, simplify, ct);
        await client.AddExpenseAsync(group.Id,
            ApiExtensions.ExpenseBody("Thirds", 3m, [(a, 1m), (b, 2m)], [(c, 1m), (d, 1m), (e, 1m)]), ct);

        var balances = await client.GetBalancesAsync(group.Id, ct);
        balances.NetOf(a).ShouldBe(1m);
        balances.NetOf(b).ShouldBe(2m);
        foreach (var debtor in new[] { c, d, e }) balances.NetOf(debtor).ShouldBe(-1m);
        balances.ShouldNetToZero();
        foreach (var member in balances.Members)
            member.Net.ShouldBe(balances.Debts.Where(x => x.CreditorMemberId == member.MemberId).Sum(x => x.Amount)
                - balances.Debts.Where(x => x.DebtorMemberId == member.MemberId).Sum(x => x.Amount));
        var overview = (await client.GetOverviewAsync(group.Id, ct)).MyDebtSummary;
        overview.Net.ShouldBe(1m);
        var overall = await client.GetMyBalanceAsync(ct);
        overall.OwedToMeTotal.ShouldBe(overview.PaidTotal);
        overall.IOweTotal.ShouldBe(overview.OwedTotal);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var split = await db.ExpenseSplits.SingleAsync(x => x.Expense.GroupId == group.Id && x.OwedByMemberId == e, ct);
        split.Amount = .9999m;
        await db.SaveChangesAsync(ct);
        await (await client.GetAsync($"/api/Group/{group.Id}/balances", ct))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "financial_data_inconsistent", ct);
        await db.Entry(split).ReloadAsync(ct);
        split.Amount.ShouldBe(.9999m);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    public async Task RedirectedSettlementStaysConsistentAcrossModes(decimal amount)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Debts", ct);
        var a = await client.MyMemberIdAsync(group.Id, ct);
        var b = await client.AddVirtualMemberAsync(group.Id, "B", ct);
        var c = await client.AddVirtualMemberAsync(group.Id, "C", ct);
        await client.AddExpenseAsync(group.Id, ApiExtensions.ExpenseBody("AB", 10, b, [(a, 10)]), ct);
        await client.AddExpenseAsync(group.Id, ApiExtensions.ExpenseBody("BC", 10, c, [(b, 10)]), ct);
        var regular = await client.GetBalancesAsync(group.Id, ct);
        regular.Debts.Count.ShouldBe(2);
        await SetMode(client, group.Id, true, ct);
        (await client.GetBalancesAsync(group.Id, ct)).ShouldOwe(a, c, 10);
        var payment = await client.PostAsJsonAsync($"/api/Expense/{group.Id}/settleUp", new
        { FromMemberId = a, ToMemberId = c, Amount = amount, Date = DateTime.UtcNow }, ct);
        payment.StatusCode.ShouldBe(HttpStatusCode.NoContent, await payment.Content.ReadAsStringAsync(ct));
        foreach (var mode in new[] { false, true, false, true })
        {
            await SetMode(client, group.Id, mode, ct);
            var balances = await client.GetBalancesAsync(group.Id, ct);
            balances.NetOf(a).ShouldBe(amount - 10);
            balances.ShouldNetToZero();
            balances.Debts.Count.ShouldBe(amount == 10 ? 0 : mode ? 1 : 2);
            var overview = (await client.GetOverviewAsync(group.Id, ct)).MyDebtSummary;
            var overall = await client.GetMyBalanceAsync(ct);
            overview.Net.ShouldBe(balances.NetOf(a));
            overall.IOweTotal.ShouldBe(overview.OwedTotal);
            overall.OwedToMeTotal.ShouldBe(overview.PaidTotal);
            overall.GroupCount.ShouldBe(amount == 10 ? 0 : 1);
        }
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Expenses.CountAsync(x => x.GroupId == group.Id, ct)).ShouldBe(3);
    }

    [Fact]
    public async Task OmittedSettingPreservesEnabledMode()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Before", ct);
        await SetMode(client, group.Id, true, ct);
        var response = await client.PutAsJsonAsync("/api/Group", new { Id = group.Id, Name = "After" }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<GroupResponse>($"/api/Group/{group.Id}", ct))!.SimplifyDebts.ShouldBeTrue();
        await SetMode(client, group.Id, false, ct);
        (await client.GetFromJsonAsync<GroupResponse>($"/api/Group/{group.Id}", ct))!.SimplifyDebts.ShouldBeFalse();
    }

    [Fact]
    public async Task FractionalBalancesAgreeWithListsOverviewAndRemovalGuard()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Fractions", ct);
        var me = await client.MyMemberIdAsync(group.Id, ct);
        var b = await client.AddVirtualMemberAsync(group.Id, "B", ct);
        var c = await client.AddVirtualMemberAsync(group.Id, "C", ct);
        await client.AddExpenseAsync(group.Id, ApiExtensions.ExpenseBody("Fraction", 10.005m, me, [(b, 5.0025m), (c, 5.0025m)]), ct);
        foreach (var mode in new[] { false, true })
        {
            await SetMode(client, group.Id, mode, ct);
            var balances = await client.GetBalancesAsync(group.Id, ct);
            balances.NetOf(me).ShouldBe(10.01m);
            balances.ShouldNetToZero();
            foreach (var member in balances.Members)
                member.Net.ShouldBe(balances.Debts.Where(x => x.CreditorMemberId == member.MemberId).Sum(x => x.Amount)
                    - balances.Debts.Where(x => x.DebtorMemberId == member.MemberId).Sum(x => x.Amount));
            (await client.GetMyGroupsAsync(ct)).Data.Single(x => x.Id == group.Id).NetBalance.ShouldBe(10.01m);
            (await client.GetFromJsonAsync<List<GroupResponse>>("/api/Group/recent", ct))!.Single(x => x.Id == group.Id).NetBalance.ShouldBe(10.01m);
            (await client.GetOverviewAsync(group.Id, ct)).MyDebtSummary.Net.ShouldBe(10.01m);
            (await client.GetMyBalanceAsync(ct)).Net.ShouldBe(10.01m);
        }
        var removed = await client.DeleteAsync($"/api/GroupMember/{group.Id}/members/{b}", ct);
        await removed.ShouldBeProblemAsync(HttpStatusCode.Conflict, "member_has_balance", ct);
    }

    [Fact]
    public async Task HistoricalIntermediaryRemainsInLedger()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("History", ct);
        var me = await client.MyMemberIdAsync(group.Id, ct);
        var b = await client.AddVirtualMemberAsync(group.Id, "B", ct);
        var c = await client.AddVirtualMemberAsync(group.Id, "C", ct);
        await client.AddExpenseAsync(group.Id, ApiExtensions.ExpenseBody("B owes me", 10, me, [(b, 10)]), ct);
        await client.AddExpenseAsync(group.Id, ApiExtensions.ExpenseBody("C owes B", 10, b, [(c, 10)]), ct);
        var removed = await client.DeleteAsync($"/api/GroupMember/{group.Id}/members/{b}", ct);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync(ct));
        var regular = await client.GetBalancesAsync(group.Id, ct);
        regular.Members.ShouldContain(x => x.MemberId == b && x.DisplayName == "B");
        regular.ShouldOwe(c, b, 10);
        await SetMode(client, group.Id, true, ct);
        var simplified = await client.GetBalancesAsync(group.Id, ct);
        simplified.Debts.ShouldHaveSingleItem();
        simplified.ShouldOwe(c, me, 10);
    }

    [Fact]
    public async Task MixedGroupsRemainSeparateAndExcludeUnrelatedEdges()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var first = await client.CreateGroupAsync("First", ct);
        var me = await client.MyMemberIdAsync(first.Id, ct);
        var b = await client.AddVirtualMemberAsync(first.Id, "B", ct);
        var c = await client.AddVirtualMemberAsync(first.Id, "C", ct);
        var d = await client.AddVirtualMemberAsync(first.Id, "D", ct);
        await client.AddExpenseAsync(first.Id, ApiExtensions.ExpenseBody("Me owes B", 10, b, [(me, 10)]), ct);
        await client.AddExpenseAsync(first.Id, ApiExtensions.ExpenseBody("Other edge", 50, c, [(d, 50)]), ct);
        await SetMode(client, first.Id, true, ct);
        var second = await client.CreateGroupAsync("Second", ct);
        await client.AddExistingMemberAsync(second.Id, b, ct);
        await client.AddExpenseAsync(second.Id, ApiExtensions.ExpenseBody("B owes me", 10, me, [(b, 10)]), ct);
        var result = await client.GetMyBalanceAsync(ct);
        result.Net.ShouldBe(0);
        result.IOweTotal.ShouldBe(10);
        result.OwedToMeTotal.ShouldBe(10);
        result.GroupCount.ShouldBe(2);
    }

    [Fact]
    public async Task CorruptedSourceIsReportedWithoutRepairingIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Invalid", ct);
        var me = await client.MyMemberIdAsync(group.Id, ct);
        var b = await client.AddVirtualMemberAsync(group.Id, "B", ct);
        await client.AddExpenseAsync(group.Id, ApiExtensions.ExpenseBody("Expense", 10, me, [(b, 10)]), ct);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var split = await db.ExpenseSplits.SingleAsync(x => x.Expense.GroupId == group.Id, ct);
        split.Amount = 9.9999m;
        await db.SaveChangesAsync(ct);
        foreach (var path in new[] { $"/api/Group/{group.Id}/balances", $"/api/Group/{group.Id}/overview", "/api/Balance/me", "/api/Group?skip=0&take=50", "/api/Group/recent" })
        {
            var response = await client.GetAsync(path, ct);
            await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "financial_data_inconsistent", ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            using var json = JsonDocument.Parse(body);
            json.RootElement.GetProperty("detail").GetString().ShouldBe("Financial data is inconsistent. Please contact support.");
            json.RootElement.GetProperty("detail").GetString()!.ShouldNotContain(group.Id.ToString());
            body.ShouldNotContain("KeyNotFoundException");
            body.ShouldNotContain("stackTrace");
        }
        await db.Entry(split).ReloadAsync(ct);
        split.Amount.ShouldBe(9.9999m);
    }
}
