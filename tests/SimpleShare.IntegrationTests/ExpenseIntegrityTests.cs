using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using simpleShare_backend.Contracts.Expenses;
using simpleShare_backend.Data;

namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class ExpenseIntegrityTests(ApiFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateForeignCreatesReuseOneCommittedSnapshot(bool concurrent)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Replay", ct);
        var me = await client.MyMemberIdAsync(group.Id, ct);
        var other = await client.AddVirtualMemberAsync(group.Id, "Other", ct);
        var id = Guid.NewGuid();
        var path = $"/api/Expense/{group.Id}";
        var rates = fixture.Services.GetRequiredService<RecordingExchangeRateProvider>();
        var before = rates.Requests.Count;
        object Body(string description) => new {
            ClientExpenseId = id, Description = description, ExpenseDate = "2026-09-13",
            OriginalCurrency = "USD", OriginalAmount = 10m,
            Payments = new[] { new { MemberId = me, OriginalAmount = 10m } },
            Splits = new[] { new { MemberId = other, OriginalAmount = 10m } }
        };
        Task<HttpResponseMessage> Post() => client.PostAsJsonAsync(path, Body("Taxi"), ct);
        var responses = concurrent ? await Task.WhenAll(Post(), Post()) : new[] { await Post(), await Post() };
        foreach (var response in responses)
        {
            var expense = await response.ReadOkAsync<ExpenseResponse>(ct);
            expense.Id.ShouldBe(id);
            expense.Amount.ShouldBe(16.70m);
            expense.ExchangeRateDate.ShouldBe(new DateOnly(2026, 9, 11));
        }
        var conflict = await client.PostAsJsonAsync(path, Body("Other expense"), ct);
        await conflict.ShouldBeProblemAsync(HttpStatusCode.Conflict, "expense_create_conflict", ct);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Expenses.CountAsync(x => x.GroupId == group.Id, ct)).ShouldBe(1);
        (await db.ExpensePayments.CountAsync(x => x.ExpenseId == id, ct)).ShouldBe(1);
        (await db.ExpenseSplits.CountAsync(x => x.ExpenseId == id, ct)).ShouldBe(1);
        (await db.Activities.CountAsync(x => x.EntityId == id, ct)).ShouldBe(1);

        (await client.PutAsJsonAsync(path, new { Id = id, Description = "Edited" }, ct)).EnsureSuccessStatusCode();
        (await (await Post()).ReadOkAsync<ExpenseResponse>(ct)).Description.ShouldBe("Edited");
        (await client.DeleteAsync($"{path}/{id}", ct)).EnsureSuccessStatusCode();
        (await (await Post()).ReadOkAsync<ExpenseResponse>(ct)).Id.ShouldBe(id);
        (await db.Expenses.SingleAsync(x => x.Id == id, ct)).DeletedAt.ShouldNotBeNull();
        rates.Requests.Count.ShouldBe(before + 1);
    }

    [Fact]
    public async Task SettlementEditRequiresDistinctMembersBeforeReplacingChildren()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Settlement edit", ct);
        var me = await client.MyMemberIdAsync(group.Id, ct);
        var other = await client.AddVirtualMemberAsync(group.Id, "Other", ct);
        var path = $"/api/Expense/{group.Id}";
        (await client.PostAsJsonAsync($"{path}/settleUp", new { ToMemberId = other, Amount = 5m, Date = "2026-09-12" }, ct)).EnsureSuccessStatusCode();
        var id = (await client.GetExpensesAsync(group.Id, ct)).Data.Single().Id;
        object Edit(Guid recipient, decimal amount) => new { Id = id, Financial = new {
            ExpenseDate = "2026-09-13", OriginalCurrency = "BAM", OriginalAmount = amount,
            Payments = new[] { new { MemberId = me, OriginalAmount = amount } },
            Splits = new[] { new { MemberId = recipient, OriginalAmount = amount } }
        }};
        var updated = await (await client.PutAsJsonAsync(path, Edit(other, 7m), ct)).ReadOkAsync<ExpenseResponse>(ct);
        updated.Amount.ShouldBe(7m);
        updated.ExpenseDate.ShouldBe(new DateOnly(2026, 9, 13));
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var activities = await db.Activities.CountAsync(x => x.EntityId == id, ct);
        var balance = await client.GetBalancesAsync(group.Id, ct);
        var detail = await client.GetStringAsync($"{path}/{id}", ct);
        var payment = await db.ExpensePayments.AsNoTracking().SingleAsync(x => x.ExpenseId == id, ct);
        var split = await db.ExpenseSplits.AsNoTracking().SingleAsync(x => x.ExpenseId == id, ct);
        await (await client.PutAsJsonAsync(path, Edit(me, 9m), ct)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invalid_request", ct);
        (await client.GetStringAsync($"{path}/{id}", ct)).ShouldBe(detail);
        var after = await client.GetBalancesAsync(group.Id, ct);
        after.NetOf(me).ShouldBe(balance.NetOf(me));
        after.NetOf(other).ShouldBe(balance.NetOf(other));
        (await db.Activities.CountAsync(x => x.EntityId == id, ct)).ShouldBe(activities);
        (await db.ExpensePayments.SingleAsync(x => x.ExpenseId == id, ct)).CreatedAt.ShouldBe(payment.CreatedAt);
        (await db.ExpenseSplits.SingleAsync(x => x.ExpenseId == id, ct)).CreatedAt.ShouldBe(split.CreatedAt);
    }
}
