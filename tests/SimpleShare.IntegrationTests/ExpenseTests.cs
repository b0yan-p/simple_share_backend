using System.Net;
using System.Net.Http.Json;
using Shouldly;
using simpleShare_backend.Contracts.Expenses;
using simpleShare_backend.Models.Enums;

namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class ExpenseTests(ApiFixture fixture)
{
    /// The caller's own display name, spelled out here rather than relying on
    /// RegisterAsync's defaults - EXP-11 and EXP-12 assert against it.
    private const string MyName = "Ada Lovelace";

    /// <summary>
    /// The shape almost every case here needs: one real caller plus two virtual
    /// members, which is the cheapest way to get a three-person group over HTTP.
    /// </summary>
    private async Task<(HttpClient Client, Guid GroupId, Guid Me, Guid Bo, Guid Cy)>
        ThreeMemberGroupAsync(CancellationToken ct, string name = "Trip")
    {
        var (client, _) = await fixture.RegisterAsync(ct, firstName: "Ada", lastName: "Lovelace");
        var group = await client.CreateGroupAsync(name, ct);
        var me = await client.MyMemberIdAsync(group.Id, ct);
        var bo = await client.AddVirtualMemberAsync(group.Id, "Bo", ct);
        var cy = await client.AddVirtualMemberAsync(group.Id, "Cy", ct);

        return (client, group.Id, me, bo, cy);
    }

    // ------------------------------------------------------------ create

    [Fact] // EXP-01
    public async Task CreateExpense_SinglePayerWithExplicitSplits_ReturnsExpense()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, cy) = await ThreeMemberGroupAsync(ct);
        var request = ApiExtensions.ExpenseBody(
            "Dinner", 30m, me, [(me, 10m), (bo, 10m), (cy, 10m)]);

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}", request, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var expense = await response.Content.ReadFromJsonAsync<ExpenseResponse>(ct);
        expense.ShouldNotBeNull();
        expense.Id.ShouldNotBe(Guid.Empty);
        expense.Description.ShouldBe("Dinner");
        expense.Amount.ShouldBe(30m);
        expense.IsSettleUp.ShouldBeFalse();

        var paid = expense.PaidBy.ShouldHaveSingleItem();
        paid.MemberId.ShouldBe(me);
        paid.Amount.ShouldBe(30m);

        expense.SplittedBy.Count.ShouldBe(3);
        expense.SplittedBy.Sum(s => s.Amount).ShouldBe(30m);
        expense.SplittedBy.Select(s => s.MemberId).ShouldBe([me, bo, cy], ignoreOrder: true);
    }

    [Fact] // EXP-02
    public async Task CreateExpense_WithEmptyPayments_ReturnsPaymentsRequired()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);
        var request = ApiExtensions.ExpenseBody("Dinner", 10m, [], [(me, 5m), (bo, 5m)]);

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}", request, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "payments_required", ct);
    }

    [Fact] // EXP-03
    public async Task CreateExpense_WithNonPositivePayment_ReturnsPaymentAmountInvalid()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);

        // Sums to the total, so only the non-positive amount can trip this.
        var request = ApiExtensions.ExpenseBody(
            "Dinner", 10m, [(me, -5m), (bo, 15m)], [(me, 5m), (bo, 5m)]);

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}", request, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "payment_amount_invalid", ct);
    }

    [Fact] // EXP-04
    public async Task CreateExpense_WithDuplicatePayer_ReturnsPaymentsNotUnique()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);
        var request = ApiExtensions.ExpenseBody(
            "Dinner", 10m, [(me, 5m), (me, 5m)], [(me, 5m), (bo, 5m)]);

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}", request, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "payments_not_unique", ct);
    }

    [Fact] // EXP-05
    public async Task CreateExpense_WhenPaymentsDoNotSumToTotal_ReturnsInvalidRequestData()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);
        var request = ApiExtensions.ExpenseBody(
            "Dinner", 10m, [(me, 5m)], [(me, 5m), (bo, 5m)]);   // 5 paid on a 10 bill

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}", request, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invalid_request_data", ct);
    }

    [Fact] // EXP-06
    public async Task CreateExpense_WithEmptySplits_ReturnsSplitsRequired()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, _, _) = await ThreeMemberGroupAsync(ct);
        var request = ApiExtensions.ExpenseBody("Dinner", 10m, [(me, 10m)], []);

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}", request, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "splits_required", ct);
    }

    [Fact] // EXP-07
    public async Task CreateExpense_WithNonPositiveSplit_ReturnsSplitAmountInvalid()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);
        var request = ApiExtensions.ExpenseBody(
            "Dinner", 10m, me, [(me, -5m), (bo, 15m)]);

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}", request, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "split_amount_invalid", ct);
    }

    [Fact] // EXP-08
    public async Task CreateExpense_WithDuplicateSplitMember_ReturnsSplitsNotUnique()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, _, _) = await ThreeMemberGroupAsync(ct);
        var request = ApiExtensions.ExpenseBody("Dinner", 10m, me, [(me, 5m), (me, 5m)]);

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}", request, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "splits_not_unique", ct);
    }

    [Fact] // EXP-09
    public async Task CreateExpense_WhenSplitsDoNotSumToTotal_ReturnsSplitsSumInvalid()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);
        var request = ApiExtensions.ExpenseBody(
            "Dinner", 30m, me, [(me, 10m), (bo, 10m)]);   // 20 split on a 30 bill

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}", request, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "splits_sum_invalid", ct);
    }

    [Fact] // EXP-10
    public async Task CreateExpense_WithPayerOutsideTheGroup_ReturnsPaymentsInvalidMembers()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);

        // A real member, but of a different group - so it exists and is still
        // not spendable here.
        var elsewhere = await client.CreateGroupAsync("Elsewhere", ct);
        var outsider = await client.AddVirtualMemberAsync(elsewhere.Id, "Outsider", ct);

        var request = ApiExtensions.ExpenseBody(
            "Dinner", 10m, [(outsider, 10m)], [(me, 5m), (bo, 5m)]);

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}", request, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "payments_invalid_members", ct);
    }

    [Fact] // EXP-10
    public async Task CreateExpense_WithSplitMemberOutsideTheGroup_ReturnsSplitsInvalidMembers()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, _, _) = await ThreeMemberGroupAsync(ct);
        var elsewhere = await client.CreateGroupAsync("Elsewhere", ct);
        var outsider = await client.AddVirtualMemberAsync(elsewhere.Id, "Outsider", ct);

        var request = ApiExtensions.ExpenseBody(
            "Dinner", 10m, me, [(me, 5m), (outsider, 5m)]);

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}", request, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "splits_invalid_members", ct);
    }

    // -------------------------------------------------------------- read

    [Fact] // EXP-11
    public async Task GetGroupExpenses_WithPaging_ReturnsNewestFirstWithCallerNet()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);
        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("One", 10m, me, [(me, 5m), (bo, 5m)]), ct);
        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Two", 20m, bo, [(me, 10m), (bo, 10m)]), ct);
        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Three", 30m, me, [(me, 15m), (bo, 15m)]), ct);

        // Act
        var page = await client.GetExpensesAsync(groupId, ct, skip: 0, take: 2);

        // Assert
        page.TotalCount.ShouldBe(3);
        page.Data.Select(e => e.Description).ShouldBe(["Three", "Two"]);

        // Net is what the caller is up on each row: paid minus their own share.
        page.Data[0].Net.ShouldBe(15m);    // paid 30, owes 15
        page.Data[1].Net.ShouldBe(-10m);   // paid 0, owes 10
        page.Data[0].PaidByMembers.ShouldBe([MyName]);
    }

    [Fact] // EXP-12
    public async Task GetExpenseById_ReturnsFullResponseWithCreatedInfo()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);
        var created = await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Taxi", 24m, me, [(me, 12m), (bo, 12m)]), ct);

        // Act
        var response = await client.GetAsync($"/api/Expense/{groupId}/{created.Id}", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var expense = await response.Content.ReadFromJsonAsync<ExpenseResponse>(ct);
        expense.ShouldNotBeNull();
        expense.Id.ShouldBe(created.Id);
        expense.Description.ShouldBe("Taxi");
        expense.Amount.ShouldBe(24m);
        expense.PaidBy.ShouldHaveSingleItem().MemberId.ShouldBe(me);
        expense.SplittedBy.Count.ShouldBe(2);

        expense.CreatedInfo.ShouldNotBeNull();
        expense.CreatedInfo.MemberId.ShouldBe(me);
        expense.CreatedInfo.MemberName.ShouldBe(MyName);
        expense.CreatedInfo.ModificationDate.ShouldNotBe(default);
        expense.ModifiedInfo.ShouldBeNull("nothing has edited this expense yet");
    }

    // ------------------------------------------------------------ modify

    [Fact] // EXP-13
    public async Task UpdateExpense_WithNewAmount_RecalculatesBalancesAndStampsModifiedInfo()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);
        var created = await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Groceries", 30m, me, [(me, 15m), (bo, 15m)]), ct);
        var request = new
        {
            Id = created.Id,
            TotalAmount = 10m,
            Payments = new[] { new { MemberId = me, Amount = 10m } },
            Splits = new[]
            {
                new { MemberId = me, Amount = 5m },
                new { MemberId = bo, Amount = 5m },
            },
        };

        // Act
        var response = await client.PutAsJsonAsync($"/api/Expense/{groupId}", request, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var updated = await response.Content.ReadFromJsonAsync<ExpenseResponse>(ct);
        updated.ShouldNotBeNull();
        updated.Amount.ShouldBe(10m);
        updated.ModifiedInfo.ShouldNotBeNull();
        updated.ModifiedInfo.MemberId.ShouldBe(me);

        var balances = await client.GetBalancesAsync(groupId, ct);
        balances.NetOf(me).ShouldBe(5m);
        balances.NetOf(bo).ShouldBe(-5m);
        balances.ShouldOwe(debtor: bo, creditor: me, amount: 5m);
        balances.ShouldNetToZero();
    }

    [Fact] // EXP-14
    public async Task DeleteExpense_ReturnsBalancesToTheirPreExpenseValues()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);
        var created = await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Concert", 20m, me, [(me, 10m), (bo, 10m)]), ct);

        // Act
        var response = await client.DeleteAsync($"/api/Expense/{groupId}/{created.Id}", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var balances = await client.GetBalancesAsync(groupId, ct);
        balances.Debts.ShouldBeEmpty();
        balances.NetOf(me).ShouldBe(0m);
        balances.NetOf(bo).ShouldBe(0m);
        balances.ShouldNetToZero();

        var expenses = await client.GetExpensesAsync(groupId, ct);
        expenses.TotalCount.ShouldBe(0);
    }

    [Fact] // EXP-15
    public async Task CreateExpense_WithTwoPayers_AllocatesDebtInProportionToWhatEachPaid()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, cy) = await ThreeMemberGroupAsync(ct);

        // I put in 20 and Bo 10 on a 30 bill split evenly three ways.
        var request = ApiExtensions.ExpenseBody(
            "Villa", 30m, [(me, 20m), (bo, 10m)], [(me, 10m), (bo, 10m), (cy, 10m)]);

        // Act
        await client.AddExpenseAsync(groupId, request, ct);

        // Assert
        var balances = await client.GetBalancesAsync(groupId, ct);

        balances.NetOf(me).ShouldBe(10m);
        balances.NetOf(bo).ShouldBe(0m);
        balances.NetOf(cy).ShouldBe(-10m);

        // Cy's 10 is owed to us in the same 2:1 ratio we paid it in.
        balances.ShouldOwe(debtor: cy, creditor: me, amount: 6.67m);
        balances.ShouldOwe(debtor: cy, creditor: bo, amount: 3.33m);

        // Bo overpaid their own share by a third of it, so they net out against us.
        balances.ShouldOwe(debtor: bo, creditor: me, amount: 3.33m);
        balances.ShouldNetToZero();
    }

    // --------------------------------------------------------- settle up

    [Fact] // SET-01
    public async Task SettleUp_InFull_LeavesThePairSettled()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);
        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Dinner", 20m, me, [(me, 10m), (bo, 10m)]), ct);
        var request = new
        {
            FromMemberId = bo,
            ToMemberId = me,
            Amount = 10m,
            Date = DateTime.UtcNow,
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}/settleUp", request, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(ct));

        var balances = await client.GetBalancesAsync(groupId, ct);
        balances.DebtBetween(debtor: bo, creditor: me).ShouldBeNull();
        balances.Members.Single(m => m.MemberId == bo).Side.ShouldBe(BalanceSide.Settled);
        balances.Members.Single(m => m.MemberId == me).Side.ShouldBe(BalanceSide.Settled);
        balances.ShouldNetToZero();
    }

    [Fact] // SET-02
    public async Task SettleUp_Partially_LeavesTheRemainderOwing()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);
        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Dinner", 20m, me, [(me, 10m), (bo, 10m)]), ct);
        var request = new
        {
            FromMemberId = bo,
            ToMemberId = me,
            Amount = 4m,
            Date = DateTime.UtcNow,
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}/settleUp", request, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(ct));

        var balances = await client.GetBalancesAsync(groupId, ct);
        balances.ShouldOwe(debtor: bo, creditor: me, amount: 6m);
        balances.NetOf(me).ShouldBe(6m);
        balances.NetOf(bo).ShouldBe(-6m);
        balances.ShouldNetToZero();
    }

    [Fact] // SET-03
    public async Task SettleUp_ToYourself_ReturnsInvalidRequest()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, _, _) = await ThreeMemberGroupAsync(ct);

        // FromMemberId omitted, so it resolves to the caller - the same member
        // the payment is aimed at.
        var request = new { ToMemberId = me, Amount = 5m, Date = DateTime.UtcNow };

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}/settleUp", request, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invalid_request", ct);
    }

    [Fact] // SET-04
    public async Task SettleUp_ToAMemberOutsideTheGroup_ReturnsForbidden()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, _, _, _) = await ThreeMemberGroupAsync(ct);
        var elsewhere = await client.CreateGroupAsync("Elsewhere", ct);
        var outsider = await client.AddVirtualMemberAsync(elsewhere.Id, "Outsider", ct);
        var request = new { ToMemberId = outsider, Amount = 5m, Date = DateTime.UtcNow };

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}/settleUp", request, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "member_not_in_group", ct);
    }

    [Fact] // SET-05
    public async Task SettleUp_AppearsInTheExpenseListFlaggedAsSettleUp()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, groupId, me, bo, _) = await ThreeMemberGroupAsync(ct);
        await client.AddExpenseAsync(groupId,
            ApiExtensions.ExpenseBody("Dinner", 20m, me, [(me, 10m), (bo, 10m)]), ct);
        var request = new
        {
            FromMemberId = bo,
            ToMemberId = me,
            Amount = 10m,
            Date = DateTime.UtcNow,
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}/settleUp", request, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(ct));

        var expenses = await client.GetExpensesAsync(groupId, ct);
        expenses.TotalCount.ShouldBe(2);

        var settleUps = expenses.Data.Where(e => e.IsSettleUp).ToList();
        settleUps.ShouldHaveSingleItem().Amount.ShouldBe(10m);
        expenses.Data.Count(e => !e.IsSettleUp).ShouldBe(1);
    }
}
