using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using simpleShare_backend.Models.Identity;

namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PasswordRecoveryTests(ApiFixture fixture)
{
    private const string Route = "/api/auth/forgot-password";

    [Fact]
    public async Task OnlyConfirmedLocalAccountsReceiveLinksAndParallelRequestsSendOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var local = await fixture.CreateUserAsync(ct, withPassword: true);
        var unconfirmed = await fixture.CreateUserAsync(ct, confirmed: false, withPassword: true);
        var external = await fixture.CreateUserAsync(ct);
        var failing = await fixture.CreateUserAsync(ct, withPassword: true);
        var absent = ApiExtensions.UniqueEmail();
        var mail = fixture.Services.GetRequiredService<RecordingEmailService>();
        mail.FailFor(failing.Email!);

        var addresses = new[] { local.Email!, local.Email!.ToUpperInvariant(), unconfirmed.Email!,
            external.Email!, absent, failing.Email! };
        var responses = await Task.WhenAll(addresses.Select(email =>
            client.PostAsJsonAsync(Route, new { email }, ct)));

        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            (await response.Content.ReadAsStringAsync(ct)).ShouldBeEmpty();
        }

        foreach (var email in addresses.Skip(2))
            mail.ResetsFor(email).ShouldBeEmpty();

        var message = mail.ResetsFor(local.Email!).ShouldHaveSingleItem();
        message.FirstName.ShouldBe(local.FirstName);
        var link = new Uri(message.ResetUrl);
        link.Host.ShouldBe("test.local");
        link.AbsolutePath.ShouldBe("/reset-password");
        link.Query.ShouldBeEmpty();
        var fragment = QueryHelpers.ParseQuery(link.Fragment[1..]);
        fragment["userId"].ToString().ShouldBe(local.Id.ToString());

        using var scope = fixture.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(fragment["token"].ToString()));
        (await users.VerifyUserTokenAsync(local, users.Options.Tokens.PasswordResetTokenProvider,
            UserManager<User>.ResetPasswordTokenPurpose, token)).ShouldBeTrue();
    }

    [Fact]
    public async Task ResetWithEmailedLinkChangesPasswordAndRejectsReuse()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var user = await fixture.CreateUserAsync(ct, withPassword: true);
        var sent = await client.PostAsJsonAsync(Route, new { user.Email }, ct);
        sent.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var mail = fixture.Services.GetRequiredService<RecordingEmailService>();
        var link = new Uri(mail.ResetsFor(user.Email!).ShouldHaveSingleItem().ResetUrl);
        var parameters = QueryHelpers.ParseQuery(link.Fragment[1..]);
        var token = parameters["token"].ToString();
        const string newPassword = "NewPass123";

        var payload = new { userId = user.Id, token, newPassword };
        var weak = await client.PostAsJsonAsync(Route, new { userId = user.Id, token, newPassword = "weak" }, ct);
        await weak.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invalid_new_password", ct);
        var result = await client.PostAsJsonAsync(Route, payload, ct);
        result.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var replay = await client.PostAsJsonAsync(Route, payload, ct);
        await replay.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invalid_reset_token", ct);

        var oldLogin = await client.PostAsJsonAsync("/api/auth/login",
            new { user.Email, Password = ApiExtensions.ValidPassword }, ct);
        oldLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var newLogin = await client.PostAsJsonAsync("/api/auth/login",
            new { user.Email, Password = newPassword }, ct);
        newLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InvalidEmailIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync(Route, new { email = "invalid" }, ct);
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_error", ct);
    }
}
