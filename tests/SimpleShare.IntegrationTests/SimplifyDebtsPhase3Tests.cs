using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using simpleShare_backend.Contracts.Group;
using simpleShare_backend.Data;
using simpleShare_backend.Services;

namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class SimplifyDebtsPhase3Tests(ApiFixture fixture)
{
    private async Task<(HttpClient Client, Guid Group, Guid J, Guid B, Guid N, Guid M)> Example(CancellationToken ct)
    {
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Phase 3", ct);
        var j = await client.MyMemberIdAsync(group.Id, ct);
        var b = await client.AddVirtualMemberAsync(group.Id, "B", ct);
        var n = await client.AddVirtualMemberAsync(group.Id, "N", ct);
        var m = await client.AddVirtualMemberAsync(group.Id, "M", ct);
        foreach (var (debtor, creditor, amount) in new[] { (b,n,8.67m), (m,n,.66m), (m,b,10m), (j,b,25m), (j,m,15m) })
            await client.AddExpenseAsync(group.Id, ApiExtensions.ExpenseBody("Original", amount, creditor, [(debtor, amount)]), ct);
        return (client, group.Id, j, b, n, m);
    }

    private static async Task Mode(HttpClient client, Guid group, bool enabled, CancellationToken ct)
    {
        var response = await client.PutAsJsonAsync("/api/Group", new { Id = group, Name = "Phase 3", SimplifyDebts = enabled }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
    }

    private static async Task Pay(HttpClient client, Guid group, Guid from, Guid to, decimal amount, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync($"/api/Expense/{group}/settleUp", new
        { FromMemberId = from, ToMemberId = to, Amount = amount, Date = DateOnly.FromDateTime(DateTime.UtcNow) }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(ct));
    }

    private async Task Consistent(HttpClient client, Guid group, Guid current, decimal net, CancellationToken ct)
    {
        var balances = await client.GetBalancesAsync(group, ct);
        balances.ShouldNetToZero();
        balances.NetOf(current).ShouldBe(net);
        foreach (var member in balances.Members)
        {
            (member.Paid - member.Owed).ShouldBe(member.Net);
            member.Net.ShouldBe(balances.Debts.Where(x => x.CreditorMemberId == member.MemberId).Sum(x => x.Amount)
                - balances.Debts.Where(x => x.DebtorMemberId == member.MemberId).Sum(x => x.Amount));
        }
        var overview = (await client.GetOverviewAsync(group, ct)).MyDebtSummary;
        overview.Net.ShouldBe(net);
        var overall = await client.GetMyBalanceAsync(ct);
        overall.OwedToMeTotal.ShouldBe(overview.PaidTotal);
        overall.IOweTotal.ShouldBe(overview.OwedTotal);
        overall.GroupCount.ShouldBe(overview.DebtEdges.Count == 0 ? 0 : 1);
        (await client.GetMyGroupsAsync(ct)).Data.Single(x => x.Id == group).NetBalance.ShouldBe(net);
        (await client.GetFromJsonAsync<List<GroupResponse>>("/api/Group/recent", ct))!.Single(x => x.Id == group).NetBalance.ShouldBe(net);
        using var scope = fixture.Services.CreateScope();
        var internalBalance = await scope.ServiceProvider.GetRequiredService<IBalanceService>().GetMemberNetInGroupAsync(group, current);
        internalBalance.net.ShouldBe(net);
        internalBalance.paid.ShouldBe(balances.Members.Single(x => x.MemberId == current).Paid);
        internalBalance.owed.ShouldBe(balances.Members.Single(x => x.MemberId == current).Owed);
    }

    [Fact]
    public async Task RequestedGraphPreservesNetAndContractAcrossModes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, group, j, b, n, m) = await Example(ct);
        string[]? schema = null;
        foreach (var enabled in new[] { false, true, false })
        {
            await Mode(client, group, enabled, ct);
            var balance = await client.GetBalancesAsync(group, ct);
            balance.NetOf(b).ShouldBe(26.33m);
            balance.NetOf(n).ShouldBe(9.33m);
            balance.NetOf(m).ShouldBe(4.34m);
            if (enabled)
            {
                balance.Debts.Count.ShouldBe(3);
                balance.ShouldOwe(j, b, 26.33m); balance.ShouldOwe(j, n, 9.33m); balance.ShouldOwe(j, m, 4.34m);
            }
            else
            {
                balance.Debts.Count.ShouldBe(5);
                balance.ShouldOwe(b, n, 8.67m); balance.ShouldOwe(m, n, .66m);
                balance.ShouldOwe(m, b, 10m); balance.ShouldOwe(j, b, 25m); balance.ShouldOwe(j, m, 15m);
            }
            using var json = JsonDocument.Parse(await client.GetStringAsync($"/api/Group/{group}/balances", ct));
            var shape = json.RootElement.EnumerateObject().Select(x => x.Name).Order().ToArray();
            if (schema == null) schema = shape; else shape.ShouldBe(schema);
            await Consistent(client, group, j, -40m, ct);
        }
    }

    [Fact]
    public async Task FullSettlementStaysClosedAndDeletingOnePaymentRestoresOnlyItsBalance()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, group, j, b, n, m) = await Example(ct);
        await Mode(client, group, true, ct);
        await Pay(client, group, j, b, 26.33m, ct);
        await Pay(client, group, j, n, 9.33m, ct); // No original J -> N debt.
        await Pay(client, group, j, m, 4.34m, ct);
        foreach (var enabled in new[] { false, true, false })
        {
            await Mode(client, group, enabled, ct);
            (await client.GetBalancesAsync(group, ct)).Debts.ShouldBeEmpty();
            await Consistent(client, group, j, 0m, ct);
        }
        var payment = (await client.GetExpensesAsync(group, ct)).Data.Single(x => x.IsSettleUp && x.Amount == 9.33m);
        await client.RemoveExpenseAsync(group, payment.Id, ct);
        foreach (var enabled in new[] { false, true })
        {
            await Mode(client, group, enabled, ct);
            var balance = await client.GetBalancesAsync(group, ct);
            balance.NetOf(b).ShouldBe(0m); balance.NetOf(m).ShouldBe(0m); balance.NetOf(n).ShouldBe(9.33m);
            if (enabled) { balance.Debts.ShouldHaveSingleItem(); balance.ShouldOwe(j, n, 9.33m); }
            await Consistent(client, group, j, -9.33m, ct);
        }
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Expenses.SingleAsync(x => x.Id == payment.Id, ct)).DeletedAt.ShouldNotBeNull();
        (await db.Expenses.CountAsync(x => x.GroupId == group, ct)).ShouldBe(8);
    }

    [Fact]
    public async Task PartialAndStaleAmountRemainActualCashMovements()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, group, j, b, _, _) = await Example(ct);
        await Mode(client, group, true, ct);
        await Pay(client, group, j, b, 10m, ct);
        foreach (var enabled in new[] { false, true })
        {
            await Mode(client, group, enabled, ct);
            (await client.GetBalancesAsync(group, ct)).NetOf(b).ShouldBe(16.33m);
            await Consistent(client, group, j, -30m, ct);
        }
        // A stale screen still submits 26.33 instead of the now remaining 16.33.
        await Pay(client, group, j, b, 26.33m, ct);
        foreach (var enabled in new[] { false, true })
        {
            await Mode(client, group, enabled, ct);
            (await client.GetBalancesAsync(group, ct)).NetOf(b).ShouldBe(-10m);
            await Consistent(client, group, j, -3.67m, ct);
        }
    }

    [Fact]
    public async Task ReusingSettledLinkCreatesOnlyNewOverpayment()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, group, j, b, n, m) = await Example(ct);
        await Mode(client, group, true, ct);
        await Pay(client, group, j, b, 26.33m, ct);
        await Pay(client, group, j, n, 9.33m, ct);
        await Pay(client, group, j, m, 4.34m, ct);
        await Pay(client, group, j, n, 9.33m, ct); // Stale link after the entire graph was settled.
        foreach (var enabled in new[] { false, true })
        {
            await Mode(client, group, enabled, ct);
            var balances = await client.GetBalancesAsync(group, ct);
            balances.NetOf(n).ShouldBe(-9.33m); balances.NetOf(b).ShouldBe(0m); balances.NetOf(m).ShouldBe(0m);
            // Only the simplified graph promises a direct net-to-net transfer. The
            // regular graph may retain zero-net intermediaries after cycle removal.
            if (enabled) { balances.Debts.ShouldHaveSingleItem(); balances.ShouldOwe(n, j, 9.33m); }
            await Consistent(client, group, j, 9.33m, ct);
        }
    }

    [Fact]
    public async Task MixedModesKeepEveryEndpointAndInternalNetConsistentPerGroup()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, first, j, b, _, _) = await Example(ct);
        var second = await client.CreateGroupAsync("Second", ct);
        await client.AddExistingMemberAsync(second.Id, b, ct);
        await client.AddExpenseAsync(second.Id, ApiExtensions.ExpenseBody("Opposite group", 40m, j, [(b, 40m)]), ct);
        foreach (var enabled in new[] { false, true })
        {
            await Mode(client, first, enabled, ct);
            await Mode(client, second.Id, !enabled, ct);
            var groups = await client.GetMyGroupsAsync(ct);
            var recent = await client.GetFromJsonAsync<List<GroupResponse>>("/api/Group/recent", ct);
            using var scope = fixture.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IBalanceService>();
            foreach (var (id, net) in new[] { (first, -40m), (second.Id, 40m) })
            {
                (await client.GetBalancesAsync(id, ct)).NetOf(j).ShouldBe(net);
                (await client.GetOverviewAsync(id, ct)).MyDebtSummary.Net.ShouldBe(net);
                groups.Data.Single(x => x.Id == id).NetBalance.ShouldBe(net);
                recent!.Single(x => x.Id == id).NetBalance.ShouldBe(net);
                (await service.GetMemberNetInGroupAsync(id, j)).net.ShouldBe(net);
            }
            var overall = await client.GetMyBalanceAsync(ct);
            overall.Net.ShouldBe(0m);
            overall.OwedToMeTotal.ShouldBe(40m);
            overall.IOweTotal.ShouldBe(40m);
            overall.GroupCount.ShouldBe(2);
        }
    }

    [Fact]
    public async Task BatchLoaderUsesConstantNumberOfSelects()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, first, _, _, _, _) = await Example(ct);
        var second = await client.CreateGroupAsync("Second", ct);
        var third = await client.CreateGroupAsync("Third", ct);
        await Mode(client, second.Id, true, ct);
        using var scope = fixture.Services.CreateScope();
        var original = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var counter = new SelectCounter();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(original.Database.GetConnectionString())
            .AddInterceptors(counter).Options;
        await using var db = new ApplicationDbContext(options);
        var service = new BalanceService(db, scope.ServiceProvider.GetRequiredService<IMemberService>(), NullLogger<BalanceService>.Instance);
        await service.GetGroupsBalancesAsync([first], ct);
        var single = counter.Count;
        counter.Count = 0;
        await service.GetGroupsBalancesAsync([first, second.Id, third.Id], ct);
        counter.Count.ShouldBe(single);
        single.ShouldBe(5);
    }

    private sealed class SelectCounter : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return new(result);
        }
    }
}
