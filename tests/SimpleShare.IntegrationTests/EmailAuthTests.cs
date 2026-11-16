using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Resend;
using Shouldly;
using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Models.Identity;
using simpleShare_backend.Services.Interfaces;

namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class EmailAuthTests(ApiFixture fixture)
{
    private RecordingEmailService Mail => fixture.Services.GetRequiredService<RecordingEmailService>();
    private static object Body(string email, string password = ApiExtensions.ValidPassword, string? repeat = null) => new
    { Email = email, Password = password, RepeatPassword = repeat ?? password, FirstName = "Email", LastName = "Test" };

    [Fact]
    public void TransportIsAlwaysLocal()
    {
        using var client = fixture.CreateClient();
        using var scope = fixture.Services.CreateScope();
        scope.ServiceProvider.GetServices<IEmailService>().ShouldHaveSingleItem().ShouldBeSameAs(Mail);
        scope.ServiceProvider.GetService<IResend>().ShouldBeNull();
    }

    [Fact]
    public async Task RegisterConfirmLoginUsesCapturedLinkAndRealJwt()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var email = ApiExtensions.UniqueEmail();
        var response = await client.PostAsJsonAsync("/api/Auth/register", Body(email), ct);
        var registered = await response.ReadOkAsync<RegisterResponse>(ct);
        registered.Email.ShouldBe(email);
        registered.EmailVerificationRequired.ShouldBeTrue();
        (await response.Content.ReadAsStringAsync(ct)).ShouldNotContain("\"token\"");
        var message = Mail.For(email).ShouldHaveSingleItem();
        var link = new Uri(message.VerificationUrl);
        link.Host.ShouldBe("test.local");
        var query = QueryHelpers.ParseQuery(link.Query);
        var payload = new { UserId = query["userId"].ToString(), Token = query["token"].ToString() };
        var blocked = await client.PostAsJsonAsync("/api/Auth/login", new { Email = email, Password = ApiExtensions.ValidPassword }, ct);
        await blocked.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "email_not_confirmed", ct);
        var confirm = await client.PostAsJsonAsync("/api/Auth/confirm-email", payload, ct);
        (await confirm.ReadOkAsync<ConfirmEmailResponse>(ct)).Confirmed.ShouldBeTrue();
        var again = await client.PostAsJsonAsync("/api/Auth/confirm-email", payload, ct);
        (await again.ReadOkAsync<ConfirmEmailResponse>(ct)).AlreadyConfirmed.ShouldBeTrue();
        var login = await client.PostAsJsonAsync("/api/Auth/login", new { Email = email, Password = ApiExtensions.ValidPassword }, ct);
        var session = await login.ReadOkAsync<LoginUserResponse>(ct);
        client.UseBearer(session.Token);
        (await client.GetAsync("/api/UserConnection", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("weak", "weak", "registration_failed")]
    [InlineData(ApiExtensions.ValidPassword, "different", "passwords_mismatch")]
    public async Task InvalidRegistrationDoesNotSend(string password, string repeat, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var email = ApiExtensions.UniqueEmail();
        var response = await client.PostAsJsonAsync("/api/Auth/register", Body(email, password, repeat), ct);
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, code, ct);
        Mail.For(email).ShouldBeEmpty();
    }

    [Fact]
    public async Task ResendIsSilentForUnknownConfirmedAndCoolingDownUsers()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var pending = await fixture.CreateUserAsync(ct, confirmed: false);
        var confirmed = await fixture.CreateUserAsync(ct);
        foreach (var email in new[] { ApiExtensions.UniqueEmail(), confirmed.Email!, pending.Email!, pending.Email! })
        {
            var response = await client.PostAsJsonAsync("/api/Auth/resend-verification", new { Email = email }, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        Mail.For(confirmed.Email!).ShouldBeEmpty();
        Mail.For(pending.Email!).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task FailedRegistrationSendKeepsAccountAndRetryPreservesPassword()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var email = ApiExtensions.UniqueEmail();
        Mail.FailFor(email);
        var response = await client.PostAsJsonAsync("/api/Auth/register", Body(email), ct);
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "verification_email_failed", ct);
        Mail.For(email).ShouldBeEmpty();
        using var scope = fixture.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await manager.FindByEmailAsync(email);
        user.ShouldNotBeNull();
        user.EmailConfirmed.ShouldBeFalse();
        (await manager.GetRolesAsync(user)).ShouldContain("User");
        Mail.RecoverFor(email);
        var retry = await client.PostAsJsonAsync("/api/Auth/register", Body(email, "Different1"), ct);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        Mail.For(email).ShouldHaveSingleItem();
        var same = await manager.FindByEmailAsync(email);
        same!.Id.ShouldBe(user.Id);
        (await manager.CheckPasswordAsync(same, ApiExtensions.ValidPassword)).ShouldBeTrue();
    }

    [Fact]
    public async Task InvalidConfirmationReturnsControlledError()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var user = await fixture.CreateUserAsync(ct, confirmed: false);
        var response = await client.PostAsJsonAsync("/api/Auth/confirm-email", new { UserId = user.Id, Token = "broken" }, ct);
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invalid_token", ct);
    }

    [Fact]
    public async Task ConfirmedDuplicateRegistrationDoesNotSend()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var user = await fixture.CreateUserAsync(ct, withPassword: true);
        var response = await client.PostAsJsonAsync("/api/Auth/register", Body(user.Email!), ct);
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "registration_failed", ct);
        Mail.For(user.Email!).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Forbidden, "email_not_confirmed")]
    [InlineData(false, HttpStatusCode.Unauthorized, "invalid_credentials")]
    public async Task UnconfirmedLoginChecksPasswordBeforeRevealingConfirmation(bool correctPassword, HttpStatusCode status, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var user = await fixture.CreateUserAsync(ct, confirmed: false, withPassword: true);
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        { user.Email, Password = correctPassword ? ApiExtensions.ValidPassword : "wrong" }, ct);
        await response.ShouldBeProblemAsync(status, code, ct);
        Mail.For(user.Email!).ShouldBeEmpty();
    }

    [Fact]
    public async Task DefaultIdentitySetupHasMemberButNoGroupsOrEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, user) = await fixture.CreateAuthenticatedClientAsync(ct);
        var balance = await client.GetMyBalanceAsync(ct);
        balance.Net.ShouldBe(0);
        balance.GroupCount.ShouldBe(0);
        using var scope = fixture.Services.CreateScope();
        var member = await scope.ServiceProvider.GetRequiredService<simpleShare_backend.Services.IMemberService>().GetMemberByUserIdAsync(user.Id);
        member.ShouldNotBeNull();
        Mail.For(user.Email).ShouldBeEmpty();
    }

    [Fact]
    public async Task ResendFailureDoesNotConsumeCooldown()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var user = await fixture.CreateUserAsync(ct, confirmed: false);
        Mail.FailFor(user.Email!);
        var failed = await client.PostAsJsonAsync("/api/Auth/resend-verification", new { user.Email }, ct);
        await failed.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "verification_email_failed", ct);
        Mail.RecoverFor(user.Email!);
        var retry = await client.PostAsJsonAsync("/api/Auth/resend-verification", new { user.Email }, ct);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        Mail.For(user.Email!).ShouldHaveSingleItem();
    }
}
