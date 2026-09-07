using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using simpleShare_backend.Common;
using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Contracts.Balances;
using simpleShare_backend.Contracts.Expenses;
using simpleShare_backend.Contracts.Group;
using simpleShare_backend.Contracts.Invitation;
using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Models.Application;

namespace SimpleShare.IntegrationTests;

/// <summary>
/// Shared setup and assertion helpers. Anything a second test needs belongs here,
/// so test bodies stay scenario / action / assertion and nothing else.
/// </summary>
public static class ApiExtensions
{
    /// Satisfies the configured Identity policy: digit, lower, upper, 6+ chars,
    /// no symbol required. One password for every test that needs a valid one.
    public const string ValidPassword = "Passw0rd1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Registers a fresh user with a unique email and returns a client already
    /// carrying its bearer token. Tests never share users - that is what removes
    /// the need to reset the database between them.
    /// </summary>
    public static async Task<(HttpClient Client, LoginUserResponse User)> RegisterAsync(
        this ApiFixture fixture,
        CancellationToken ct,
        string firstName = "Test",
        string lastName = "User")
    {
        var client = fixture.CreateClient();
        var email = UniqueEmail();

        var response = await client.PostAsJsonAsync("/api/Auth/register", new
        {
            Email = email,
            Password = ValidPassword,
            RepeatPassword = ValidPassword,
            FirstName = firstName,
            LastName = lastName,
        }, ct);

        // An assertion inside a helper, deliberately: it keeps setup failures
        // distinguishable from the behaviour under test, without putting an
        // assert in any test's Arrange block.
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var user = await response.Content.ReadFromJsonAsync<LoginUserResponse>(ct)
            ?? throw new InvalidOperationException("register returned an empty body");

        client.UseBearer(user.Token);
        return (client, user);
    }

    public static string UniqueEmail() => $"u{Guid.NewGuid():N}@test.local";

    public static void UseBearer(this HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    /// <summary>
    /// Asserts the full error contract in one call: status, problem+json content
    /// type, the `code` extension the mobile client branches on, and the presence
    /// of `traceId`. The response body is attached to every failure message.
    /// </summary>
    public static async Task ShouldBeProblemAsync(
        this HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        response.StatusCode.ShouldBe(status, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json", body);

        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString().ShouldBe(code, body);
        document.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue(body);
    }

    // -------------------------------------------------------------- groups

    /// <summary>
    /// Creates a group owned by the caller. Side effect worth knowing: this is
    /// also what materialises the caller's Member row - registering alone does
    /// not create one, so any test needing a member id starts here.
    /// </summary>
    public static async Task<GroupResponse> CreateGroupAsync(
        this HttpClient client, string name, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/Group", new { Name = name }, ct);
        return await response.ReadOkAsync<GroupResponse>(ct);
    }

    public static async Task<GroupOverviewResponse> GetOverviewAsync(
        this HttpClient client, Guid groupId, CancellationToken ct)
    {
        var response = await client.GetAsync($"/api/Group/{groupId}/overview", ct);
        return await response.ReadOkAsync<GroupOverviewResponse>(ct);
    }

    /// <summary>
    /// The caller's own member id inside a group. The overview endpoint's
    /// CurrentMemberId is the only place the API hands it out.
    /// </summary>
    public static async Task<Guid> MyMemberIdAsync(
        this HttpClient client, Guid groupId, CancellationToken ct) =>
        (await client.GetOverviewAsync(groupId, ct)).CurrentMemberId;

    /// <summary>
    /// Gives a freshly registered user a Member row and returns its id, by
    /// creating a group only that user belongs to. Needed whenever a second real
    /// user has to be added to someone else's group by member id.
    /// </summary>
    public static async Task<Guid> CreateMemberIdentityAsync(
        this HttpClient client, CancellationToken ct)
    {
        var ownGroup = await client.CreateGroupAsync("Own", ct);
        return await client.MyMemberIdAsync(ownGroup.Id, ct);
    }

    public static async Task<PageResponse<GroupResponse>> GetMyGroupsAsync(
        this HttpClient client, CancellationToken ct, int skip = 0, int take = 50)
    {
        var response = await client.GetAsync($"/api/Group?skip={skip}&take={take}", ct);
        return await response.ReadOkAsync<PageResponse<GroupResponse>>(ct);
    }

    // ------------------------------------------------------------- members

    /// <summary>
    /// Posts one item to members/bulk. Takes the raw item rather than typed
    /// arguments so validation tests can send deliberately incomplete ones.
    /// </summary>
    public static async Task<HttpResponseMessage> AddMemberAsync(
        this HttpClient client, Guid groupId, object member, CancellationToken ct) =>
        await client.PostAsJsonAsync(
            $"/api/GroupMember/{groupId}/members/bulk", new { Members = new[] { member } }, ct);

    /// <summary>Adds a virtual member (no user account) and returns its member id.</summary>
    public static async Task<Guid> AddVirtualMemberAsync(
        this HttpClient client, Guid groupId, string displayName, CancellationToken ct)
    {
        var response = await client.AddMemberAsync(groupId, new { DisplayName = displayName }, ct);
        var body = await response.ReadOkAsync<AddGroupMembersResponse>(ct);

        // members/bulk answers 200 with per-item errors instead of a 4xx, so
        // without this a broken setup would surface much later and much more
        // confusingly, as a "member not in group" on the expense under test.
        body.ErrorMessages.ShouldBeEmpty();
        return body.Members.ShouldHaveSingleItem().MemberId;
    }

    /// <summary>Adds an already-existing member (another real user) to the group.</summary>
    public static async Task<GroupMemberResponse> AddExistingMemberAsync(
        this HttpClient client, Guid groupId, Guid memberId, CancellationToken ct)
    {
        var response = await client.AddMemberAsync(groupId, new { MemberId = memberId }, ct);
        var body = await response.ReadOkAsync<AddGroupMembersResponse>(ct);

        body.ErrorMessages.ShouldBeEmpty();
        return body.Members.ShouldHaveSingleItem();
    }

    public static async Task<List<GroupMemberResponse>> GetMembersAsync(
        this HttpClient client, Guid groupId, CancellationToken ct)
    {
        var response = await client.GetAsync($"/api/GroupMember/{groupId}/members", ct);
        return await response.ReadOkAsync<List<GroupMemberResponse>>(ct);
    }

    /// <summary>Owner-only: soft-removes a member, setting LeftAt.</summary>
    public static async Task<HttpResponseMessage> RemoveMemberAsync(
        this HttpClient client, Guid groupId, Guid memberId, CancellationToken ct) =>
        await client.DeleteAsync($"/api/GroupMember/{groupId}/members/{memberId}", ct);

    // ---------------------------------------------------------- invitations

    /// <summary>The group's active invite link, minted on first ask.</summary>
    public static async Task<GroupInvitationResponse> GetInvitationAsync(
        this HttpClient client, Guid groupId, CancellationToken ct)
    {
        var response = await client.GetAsync($"/api/GroupInvitation/group/{groupId}", ct);
        return await response.ReadOkAsync<GroupInvitationResponse>(ct);
    }

    /// <summary>Raw so tests can drive it anonymously and assert problem bodies.</summary>
    public static async Task<HttpResponseMessage> GetInvitePreviewAsync(
        this HttpClient client, string token, CancellationToken ct) =>
        await client.GetAsync($"/api/GroupInvitation/{token}", ct);

    public static async Task<HttpResponseMessage> JoinByTokenAsync(
        this HttpClient client, string token, CancellationToken ct) =>
        await client.PostAsync($"/api/GroupInvitation/{token}/join", content: null, ct);

    public static async Task<InvitePreviewResponse> ReadPreviewAsync(
        this HttpResponseMessage response, CancellationToken ct) =>
        await response.ReadOkAsync<InvitePreviewResponse>(ct);

    public static async Task<JoinGroupResponse> ReadJoinAsync(
        this HttpResponseMessage response, CancellationToken ct) =>
        await response.ReadOkAsync<JoinGroupResponse>(ct);

    // ------------------------------------------------------------ expenses

    /// <summary>
    /// Builds a create-expense body. Nothing is derived from anything else, so a
    /// validation test can send amounts that deliberately do not add up without
    /// needing a second builder.
    /// </summary>
    public static object ExpenseBody(
        string description,
        decimal totalAmount,
        IEnumerable<(Guid MemberId, decimal Amount)> payments,
        IEnumerable<(Guid MemberId, decimal Amount)> splits,
        bool? isSettleUp = null) => new
        {
            Description = description,
            ExpenseDate = DateTime.UtcNow,
            TotalAmount = totalAmount,
            IsSettleUp = isSettleUp,
            Payments = payments.Select(p => new { p.MemberId, p.Amount }).ToArray(),
            Splits = splits.Select(s => new { s.MemberId, s.Amount }).ToArray(),
        };

    /// <summary>One payer covering the whole amount, with explicit splits.</summary>
    public static object ExpenseBody(
        string description,
        decimal totalAmount,
        Guid payerMemberId,
        IEnumerable<(Guid MemberId, decimal Amount)> splits) =>
        ExpenseBody(description, totalAmount, [(payerMemberId, totalAmount)], splits);

    /// <summary>Splits an amount evenly, giving any leftover cent to the first member.</summary>
    public static (Guid MemberId, decimal Amount)[] EvenSplit(
        decimal totalAmount, params Guid[] memberIds)
    {
        var share = Math.Round(totalAmount / memberIds.Length, 2, MidpointRounding.ToZero);
        var remainder = totalAmount - (share * memberIds.Length);

        return [.. memberIds.Select((id, i) => (id, i == 0 ? share + remainder : share))];
    }

    public static async Task<ExpenseResponse> AddExpenseAsync(
        this HttpClient client, Guid groupId, object request, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync($"/api/Expense/{groupId}", request, ct);
        return await response.ReadOkAsync<ExpenseResponse>(ct);
    }

    public static async Task<ExpenseResponse> UpdateExpenseAsync(
        this HttpClient client, Guid groupId, object request, CancellationToken ct)
    {
        var response = await client.PutAsJsonAsync($"/api/Expense/{groupId}", request, ct);
        return await response.ReadOkAsync<ExpenseResponse>(ct);
    }

    public static async Task RemoveExpenseAsync(
        this HttpClient client, Guid groupId, Guid expenseId, CancellationToken ct)
    {
        var response = await client.DeleteAsync($"/api/Expense/{groupId}/{expenseId}", ct);

        response.StatusCode.ShouldBe(
            HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(ct));
    }

    public static async Task RemoveGroupAsync(
        this HttpClient client, Guid groupId, CancellationToken ct)
    {
        var response = await client.DeleteAsync($"/api/Group/{groupId}", ct);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
    }

    public static async Task<PageResponse<ExpenseListResponse>> GetExpensesAsync(
        this HttpClient client, Guid groupId, CancellationToken ct, int skip = 0, int take = 50)
    {
        var response = await client.GetAsync($"/api/Expense/{groupId}?skip={skip}&take={take}", ct);
        return await response.ReadOkAsync<PageResponse<ExpenseListResponse>>(ct);
    }

    // ------------------------------------------------------------ balances

    public static async Task<BalanceResponse> GetBalancesAsync(
        this HttpClient client, Guid groupId, CancellationToken ct)
    {
        var response = await client.GetAsync($"/api/Group/{groupId}/balances", ct);
        return await response.ReadOkAsync<BalanceResponse>(ct);
    }

    /// <summary>
    /// BAL-02, the invariant every balance-touching test ends on: what the group
    /// paid and what it owes are the same money, so the members' nets must cancel
    /// exactly. Catches rounding drift that per-pair assertions walk straight past.
    /// </summary>
    public static void ShouldNetToZero(this BalanceResponse balances)
    {
        var nets = balances.Members
            .Select(m => new { m.DisplayName, Net = m.Paid - m.Owed })
            .ToList();

        nets.Sum(x => x.Net).ShouldBe(
            0m,
            $"member nets: {string.Join(", ", nets.Select(x => $"{x.DisplayName} {x.Net}"))}");
    }

    /// <summary>Net for one member: positive means the group owes them.</summary>
    public static decimal NetOf(this BalanceResponse balances, Guid memberId)
    {
        var member = balances.Members.SingleOrDefault(m => m.MemberId == memberId)
            ?? throw new InvalidOperationException($"member {memberId} absent from balances");

        return member.Paid - member.Owed;
    }

    /// <summary>The debt edge between two members, or null when they are settled.</summary>
    public static DebtEdge? DebtBetween(this BalanceResponse balances, Guid debtor, Guid creditor) =>
        balances.Debts.SingleOrDefault(d =>
            d.DebtorMemberId == debtor && d.CreditorMemberId == creditor);

    /// <summary>
    /// Asserts one member owes another exactly this much. Every failure prints the
    /// whole ledger, because a wrong edge is almost never readable on its own.
    /// </summary>
    public static void ShouldOwe(
        this BalanceResponse balances, Guid debtor, Guid creditor, decimal amount)
    {
        var ledger = balances.Describe();
        var edge = balances.DebtBetween(debtor, creditor);

        edge.ShouldNotBeNull($"expected {debtor} to owe {creditor} {amount}. {ledger}");
        edge.Amount.ShouldBe(amount, ledger);
    }

    private static string Describe(this BalanceResponse balances) =>
        "Debts: " + string.Join(
            "; ",
            balances.Debts.Select(d =>
                $"{d.DebtorDisplayName} owes {d.CreditorDisplayName} {d.Amount}"));

    // -------------------------------------------------------------- shared

    /// <summary>
    /// Asserts 200 and deserialises, attaching the raw body to the failure. Keeps
    /// a broken setup step from surfacing as a bare NullReferenceException three
    /// lines later.
    /// </summary>
    private static async Task<T> ReadOkAsync<T>(
        this HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException($"{typeof(T).Name} deserialised to null: {body}");
    }
}
