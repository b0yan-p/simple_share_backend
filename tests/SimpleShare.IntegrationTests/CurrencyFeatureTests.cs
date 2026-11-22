using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using simpleShare_backend.Contracts.Expenses;
using simpleShare_backend.Contracts.Group;
using simpleShare_backend.Contracts.Balances;
using simpleShare_backend.Data;
namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class CurrencyFeatureTests(ApiFixture fixture)
{
    private object Input(Guid payer, Guid other, decimal amount = 100m, string currency = "USD", string date = "2026-09-13") => new
    {
        ExpenseDate = date,
        OriginalAmount = amount,
        OriginalCurrency = currency,
        Payments = new[] { new { MemberId = payer, OriginalAmount = amount } },
        Splits = new[] { new { MemberId = payer, OriginalAmount = amount * .3m }, new { MemberId = other, OriginalAmount = amount * .7m } }
    };
    [Fact]
    public async Task ForeignSnapshotEditReuseAndCurrencyLock()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("FX", ct);
        var me = await client.MyMemberIdAsync(group.Id, ct);
        var other = await client.AddVirtualMemberAsync(group.Id, "Other", ct);
        var provider = fixture.Services.GetRequiredService<RecordingExchangeRateProvider>();
        var before = provider.Requests.Count;
        var created = await client.PostAsJsonAsync($"/api/Expense/{group.Id}", new
        {
            ClientExpenseId = Guid.NewGuid(),
            Description = "Dinner",
            ExpenseDate = "2026-09-13",
            OriginalAmount = 100m,
            OriginalCurrency = "USD",
            Payments = new[] { new { MemberId = me, OriginalAmount = 60m }, new { MemberId = other, OriginalAmount = 40m } },
            Splits = new[] { new { MemberId = me, OriginalAmount = 30m }, new { MemberId = other, OriginalAmount = 70m } }
        }, ct);
        var expense = await created.ReadOkAsync<ExpenseResponse>(ct);
        expense.Amount.ShouldBe(167m); expense.OriginalAmount.ShouldBe(100m); expense.GroupCurrency.ShouldBe("BAM");
        expense.ExchangeRate.ShouldBe(1.67m); expense.ExchangeRateDate.ShouldBe(new DateOnly(2026, 9, 11));
        expense.PaidBy.Single(x => x.MemberId == me).OriginalAmount.ShouldBe(60m);
        expense.PaidBy.Sum(x => x.Amount).ShouldBe(167m); expense.SplittedBy.Sum(x => x.Amount).ShouldBe(167m);
        provider.Requests.Count.ShouldBe(before + 1);
        var balances = await client.GetBalancesAsync(group.Id, ct);
        balances.NetOf(me).ShouldBe(50.10m); balances.NetOf(other).ShouldBe(-50.10m);
        (await client.PutAsJsonAsync("/api/Group", new { Id = group.Id, SimplifyDebts = true }, ct)).EnsureSuccessStatusCode();
        (await client.GetBalancesAsync(group.Id, ct)).NetOf(me).ShouldBe(50.10m);
        (await client.PutAsJsonAsync($"/api/Expense/{group.Id}", new { Id = expense.Id, Description = "Updated" }, ct)).EnsureSuccessStatusCode();
        provider.Requests.Count.ShouldBe(before + 1);
        var updated = await (await client.PutAsJsonAsync($"/api/Expense/{group.Id}", new { Id = expense.Id, Financial = Input(me, other, 120m) }, ct)).ReadOkAsync<ExpenseResponse>(ct);
        updated.Amount.ShouldBe(200.4m); updated.ExchangeRateDate.ShouldBe(expense.ExchangeRateDate); provider.Requests.Count.ShouldBe(before + 1);
        (await client.PutAsJsonAsync($"/api/Expense/{group.Id}", new { Id = expense.Id, Financial = Input(me, other, 120m, "EUR") }, ct)).EnsureSuccessStatusCode();
        provider.Requests.Count.ShouldBe(before + 2);
        (await client.PutAsJsonAsync($"/api/Expense/{group.Id}", new { Id = expense.Id, Financial = Input(me, other, 120m, "EUR", "2026-09-14") }, ct)).EnsureSuccessStatusCode();
        provider.Requests.Count.ShouldBe(before + 3);
        await client.DeleteAsync($"/api/Expense/{group.Id}/{expense.Id}", ct);
        var locked = await client.PutAsJsonAsync("/api/Group", new { Id = group.Id, Currency = "EUR" }, ct);
        await locked.ShouldBeProblemAsync(HttpStatusCode.Conflict, "group_currency_locked", ct);
    }
    [Fact]
    public async Task GroupOwnerCanChangeOnlyEmptyGroupAndOverallBalancesUseCurrencyBuckets()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        foreach (var currency in new[] { "BAM", "EUR" })
        {
            var group = await client.CreateGroupAsync(currency, ct);
            (await client.PutAsJsonAsync("/api/Group", new { Id = group.Id, Currency = currency }, ct)).EnsureSuccessStatusCode();
            var me = await client.MyMemberIdAsync(group.Id, ct);
            var other = await client.AddVirtualMemberAsync(group.Id, "Other", ct);
            await client.PostAsJsonAsync($"/api/Expense/{group.Id}", new
            {
                ClientExpenseId = Guid.NewGuid(),
                Description = "Identity",
                ExpenseDate = "2026-09-13",
                OriginalCurrency = currency,
                OriginalAmount = 10m,
                Payments = new[] { new { MemberId = me, OriginalAmount = 10m } },
                Splits = new[] { new { MemberId = other, OriginalAmount = 10m } }
            }, ct);
        }
        var overall = await client.GetFromJsonAsync<OverallBalanceResponse>("/api/Balance/me", ct);
        overall!.Balances.Count.ShouldBe(2); overall.GroupCount.ShouldBe(2);
        overall.Balances.ShouldAllBe(x => x.Net == 10m);
    }
    [Fact]
    public async Task LegacyNonFinancialEditPreservesFourDecimalsAndTimestamp()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Legacy", ct);
        var me = await client.MyMemberIdAsync(group.Id, ct);
        var other = await client.AddVirtualMemberAsync(group.Id, "Other", ct);
        var expense = await client.AddExpenseAsync(group.Id, ApiExtensions.ExpenseBody("Legacy", 10m, me, [(other, 10m)]), ct);
        var date = new DateTime(2026, 9, 13, 17, 23, 42, DateTimeKind.Utc);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Expenses.Where(x => x.Id == expense.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.TotalAmount, 10.005m).SetProperty(e => e.OriginalAmount, 10.005m).SetProperty(e => e.ExchangeRateDate, (DateOnly?)null).SetProperty(e => e.ExpenseDate, date), ct);
            await db.ExpensePayments.Where(x => x.ExpenseId == expense.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.Amount, 10.005m).SetProperty(e => e.OriginalAmount, 10.005m), ct);
            await db.ExpenseSplits.Where(x => x.ExpenseId == expense.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.Amount, 10.005m).SetProperty(e => e.OriginalAmount, 10.005m), ct);
        }
        var updated = await (await client.PutAsJsonAsync($"/api/Expense/{group.Id}", new { Id = expense.Id, Description = "Metadata" }, ct)).ReadOkAsync<ExpenseResponse>(ct);
        updated.Amount.ShouldBe(10.005m); updated.OriginalAmount.ShouldBe(10.005m); updated.ExchangeRateDate.ShouldBeNull();
        using var read = fixture.Services.CreateScope();
        (await read.ServiceProvider.GetRequiredService<ApplicationDbContext>().Expenses.SingleAsync(x => x.Id == expense.Id, ct)).ExpenseDate.ShouldBe(date);
    }
    [Fact]
    public async Task CurrencyChangeAndFirstExpenseSerializeOnGroupRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Race", ct);
        var me = await client.MyMemberIdAsync(group.Id, ct);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var held = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Groups\" WHERE \"Id\" = {group.Id} FOR UPDATE", ct);
        var create = client.PostAsJsonAsync($"/api/Expense/{group.Id}", new
        {
            ClientExpenseId = Guid.NewGuid(),
            Description = "Race",
            ExpenseDate = "2026-09-13",
            OriginalAmount = 10m,
            OriginalCurrency = "USD",
            Payments = new[] { new { MemberId = me, OriginalAmount = 10m } },
            Splits = new[] { new { MemberId = me, OriginalAmount = 10m } }
        }, ct);
        var update = client.PutAsJsonAsync("/api/Group", new { Id = group.Id, Currency = "EUR" }, ct);
        await Task.Delay(100, ct);
        create.IsCompleted.ShouldBeFalse(); update.IsCompleted.ShouldBeFalse();
        await held.CommitAsync(ct);
        var expense = await (await create).ReadOkAsync<ExpenseResponse>(ct);
        var change = await update;
        (change.StatusCode == HttpStatusCode.OK || change.StatusCode == HttpStatusCode.Conflict).ShouldBeTrue();
        var finalGroup = await client.GetFromJsonAsync<GroupResponse>($"/api/Group/{group.Id}", ct);
        expense.GroupCurrency.ShouldBe(finalGroup!.Currency);
        finalGroup.CurrencyLocked.ShouldBeTrue(); finalGroup.CanChangeCurrency.ShouldBeFalse();
    }
    [Fact]
    public async Task GroupCurrencyChangeRequiresOwnerAndIdentityNeverCallsProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await owner.CreateGroupAsync("Owner", ct);
        var (member, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var invitation = await owner.GetFromJsonAsync<simpleShare_backend.Contracts.Invitation.GroupInvitationResponse>($"/api/GroupInvitation/group/{group.Id}", ct);
        (await member.PostAsync($"/api/GroupInvitation/{invitation!.Token}/join", null, ct)).EnsureSuccessStatusCode();
        await (await member.PutAsJsonAsync("/api/Group", new { Id = group.Id, Currency = "EUR" }, ct)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "group_currency_owner_required", ct);
        var me = await owner.MyMemberIdAsync(group.Id, ct);
        var provider = fixture.Services.GetRequiredService<RecordingExchangeRateProvider>(); var before = provider.Requests.Count;
        var expense = await owner.AddExpenseAsync(group.Id, ApiExtensions.ExpenseBody("Identity", 10m, me, [(me, 10m)]), ct);
        expense.ExchangeRate.ShouldBe(1m); expense.OriginalAmount.ShouldBe(expense.Amount); provider.Requests.Count.ShouldBe(before);
        var list = await owner.GetExpensesAsync(group.Id, ct); list.Data.Single().OriginalCurrency.ShouldBe("BAM");
        var listJson = await owner.GetStringAsync($"/api/Expense/{group.Id}?skip=0&take=10", ct);
        listJson.ShouldNotContain("exchangeRate");
    }
    [Fact]
    public async Task ProviderFailuresAreAtomicAndUnsupportedInputIsActionable()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Failures", ct); var me = await client.MyMemberIdAsync(group.Id, ct);
        var provider = fixture.Services.GetRequiredService<RecordingExchangeRateProvider>();
        foreach (var failure in new Exception[] { new simpleShare_backend.Common.Exceptions.ExchangeRateUnavailableException(), new simpleShare_backend.Common.Exceptions.ExchangeRateInvalidResponseException() })
        {
            provider.NextFailure = failure;
            var response = await client.PostAsJsonAsync($"/api/Expense/{group.Id}", new { ClientExpenseId = Guid.NewGuid(), Description = "Failure", ExpenseDate = "2026-09-13", OriginalAmount = 10m, OriginalCurrency = "USD", Payments = new[] { new { MemberId = me, OriginalAmount = 10m } }, Splits = new[] { new { MemberId = me, OriginalAmount = 10m } } }, ct);
            response.StatusCode.ShouldBe(failure is simpleShare_backend.Common.Exceptions.ExchangeRateUnavailableException ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.BadGateway);
        }
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Expenses.CountAsync(x => x.GroupId == group.Id, ct)).ShouldBe(0);
        (await db.Activities.CountAsync(x => x.GroupId == group.Id, ct)).ShouldBe(0);
        var invalid = await client.PostAsJsonAsync($"/api/Expense/{group.Id}", new { ClientExpenseId = Guid.NewGuid(), Description = "Invalid", ExpenseDate = "2026-09-13", OriginalAmount = 10m, OriginalCurrency = "BTC", Payments = new[] { new { MemberId = me, OriginalAmount = 10m } }, Splits = new[] { new { MemberId = me, OriginalAmount = 10m } } }, ct);
        await invalid.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "currency_not_supported", ct);
    }
    [Fact]
    public async Task LockedCreationReloadsPreviouslyTrackedGroupCurrency()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, user) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Tracked", ct); var me = await client.MyMemberIdAsync(group.Id, ct);
        using var scope = fixture.Services.CreateScope();
        var groups = scope.ServiceProvider.GetRequiredService<simpleShare_backend.Services.IGroupService>();
        await groups.GetGroupAsync(group.Id);
        (await client.PutAsJsonAsync("/api/Group", new { Id = group.Id, Currency = "EUR" }, ct)).EnsureSuccessStatusCode();
        var expenses = scope.ServiceProvider.GetRequiredService<simpleShare_backend.Services.IExpenseService>();
        var created = await expenses.SaveExpenseAsync(group.Id, new CreateExpenseRequest
        {
            ClientExpenseId = Guid.NewGuid(),
            Description = "Pending original BAM",
            ExpenseDate = new DateOnly(2026, 9, 13),
            OriginalAmount = 10m,
            OriginalCurrency = "BAM",
            Payments = [new() { MemberId = me, OriginalAmount = 10m }],
            Splits = [new() { MemberId = me, OriginalAmount = 10m }]
        }, user.Id, ct);
        created.GroupCurrency.ShouldBe("EUR"); created.OriginalCurrency.ShouldBe("BAM");
    }
}
