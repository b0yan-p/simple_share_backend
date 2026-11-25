using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Contracts.Profile;
using simpleShare_backend.Data;

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
