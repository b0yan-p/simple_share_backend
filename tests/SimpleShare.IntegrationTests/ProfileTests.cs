using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Contracts.Profile;
using simpleShare_backend.Data;
using simpleShare_backend.Models.Enums;

namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class ProfileTests(ApiFixture fixture)
{
    private const string Route = "/api/profile/me";

    [Fact]
    public async Task SavedProfileIsScopedToCallerAndReturnedOnNextLogin()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, user) = await fixture.CreateAuthenticatedClientAsync(ct, withPassword: true);
        var (other, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var before = await other.GetFromJsonAsync<ProfileDetails>(Route, ct);
        var input = new ProfileDetails("  Ana ", "", " Orb ");
        var expected = new ProfileDetails("Ana", "", "Orb");

        var saved = await client.PutAsJsonAsync(Route, input, ct);
        (await saved.ReadOkAsync<ProfileDetails>(ct)).ShouldBe(expected);
        (await client.GetFromJsonAsync<ProfileDetails>(Route, ct)).ShouldBe(expected);
        (await other.GetFromJsonAsync<ProfileDetails>(Route, ct)).ShouldBe(before);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var member = await db.Members.SingleAsync(m => m.UserId == user.Id, ct);
        member.FirstName.ShouldBe("Ana");
        member.LastName.ShouldBe("");

        var login = await fixture.CreateClient().PostAsJsonAsync("/api/Auth/login",
            new { user.Email, Password = ApiExtensions.ValidPassword }, ct);
        (await login.ReadOkAsync<LoginUserResponse>(ct)).DisplayName.ShouldBe("Orb");
    }

    [Fact]
    public async Task DeletePreservesMembersAndExpensesAndTransfersOnlyToRegisteredMembers()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, user) = await fixture.CreateAuthenticatedClientAsync(ct);
        var (friend, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var shared = await owner.CreateGroupAsync("Shared", ct);
        var privateGroup = await owner.CreateGroupAsync("Private", ct);
        await owner.AddVirtualMemberAsync(privateGroup.Id, "Guest", ct);
        var me = await owner.MyMemberIdAsync(shared.Id, ct);
        await owner.AddVirtualMemberAsync(shared.Id, "First joiner", ct);
        var successor = await friend.CreateMemberIdentityAsync(ct);
        (await owner.AddMemberAsync(shared.Id, new { MemberId = successor }, ct)).EnsureSuccessStatusCode();
        var expense = ApiExtensions.ExpenseBody("Dinner", 20m, me, [(me, 10m), (successor, 10m)]);
        (await owner.PostAsJsonAsync($"/api/Expense/{shared.Id}", expense, ct)).EnsureSuccessStatusCode();

        (await owner.DeleteAsync(Route, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync(Route, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var memberships = await friend.GetMembersAsync(shared.Id, ct);
        memberships.Single(m => m.MemberId == successor).GroupRole.ShouldBe(GroupRole.Owner);
        memberships.Single(m => m.MemberId == me).GroupRole.ShouldBe(GroupRole.Member);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.AnyAsync(u => u.Id == user.Id, ct)).ShouldBeFalse();
        var member = await db.Members.SingleAsync(m => m.Id == me, ct);
        member.UserId.ShouldBeNull();
        member.DisplayName.ShouldBe(user.DisplayName);
        member.Email.ShouldBeNull();
        member.NormalizedEmail.ShouldBeNull();
        member.FirstName.ShouldBeNull();
        member.LastName.ShouldBeNull();
        (await db.ExpensePayments.AnyAsync(p => p.PaidByMemberId == me && p.Amount == 20m, ct)).ShouldBeTrue();
        (await db.GroupMembers.AnyAsync(m => m.GroupId == privateGroup.Id && m.GroupRole == GroupRole.Owner, ct)).ShouldBeFalse();
        (await db.UserConnections.AnyAsync(c => c.UserAId == user.Id || c.UserBId == user.Id, ct)).ShouldBeFalse();
        var replacement = await fixture.CreateUserAsync(ct, email: user.Email);
        (await db.Members.SingleAsync(m => m.UserId == replacement.Id, ct)).Id.ShouldNotBe(me);
    }

    [Fact]
    public async Task DeleteSupportsAnAccountWithoutAMemberAndRequiresAuthentication()
    {
        var ct = TestContext.Current.CancellationToken;
        (await fixture.CreateClient().DeleteAsync(Route, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var client = await fixture.CreateClientWithoutMemberAsync(ct);
        (await client.DeleteAsync(Route, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync(Route, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConcurrentDeletionOfOwnerAndSuccessorLeavesAnActiveRegisteredOwner()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var (first, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var (last, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await owner.CreateGroupAsync("Concurrent deletion", ct);
        var firstId = await first.CreateMemberIdentityAsync(ct);
        var lastId = await last.CreateMemberIdentityAsync(ct);
        (await owner.AddMemberAsync(group.Id, new { MemberId = firstId }, ct)).EnsureSuccessStatusCode();
        (await owner.AddMemberAsync(group.Id, new { MemberId = lastId }, ct)).EnsureSuccessStatusCode();
        var results = await Task.WhenAll(owner.DeleteAsync(Route, ct), first.DeleteAsync(Route, ct));
        results.ShouldAllBe(r => r.StatusCode == HttpStatusCode.NoContent);
        var members = await last.GetMembersAsync(group.Id, ct);
        members.Where(m => m.GroupRole == GroupRole.Owner).ShouldHaveSingleItem().MemberId.ShouldBe(lastId);
    }

    [Theory]
    [InlineData(" ", 2, false)]
    [InlineData("Ana", 1, false)]
    [InlineData("Ana", 2, true)]
    [InlineData("Ana", 80, true)]
    [InlineData("Ana", 81, false)]
    public async Task UpdateValidatesTrimmedNames(string firstName, int length, bool valid)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var before = await client.GetFromJsonAsync<ProfileDetails>(Route, ct);
        var input = new ProfileDetails(firstName, "", " " + new string('a', length) + " ");
        var response = await client.PutAsJsonAsync(Route, input, ct);
        response.StatusCode.ShouldBe(valid ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        if (!valid) (await client.GetFromJsonAsync<ProfileDetails>(Route, ct)).ShouldBe(before);
    }

    [Fact]
    public async Task BothEndpointsRequireAuthenticationAndAnExistingProfile()
    {
        var ct = TestContext.Current.CancellationToken;
        var guest = fixture.CreateClient();
        var withoutMember = await fixture.CreateClientWithoutMemberAsync(ct);
        var input = new ProfileDetails("Ana", "", "Orb");
        foreach (var (client, expected) in new[] { (guest, HttpStatusCode.Unauthorized), (withoutMember, HttpStatusCode.NotFound) })
        {
            (await client.GetAsync(Route, ct)).StatusCode.ShouldBe(expected);
            (await client.PutAsJsonAsync(Route, input, ct)).StatusCode.ShouldBe(expected);
        }
    }
}
