using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using simpleShare_backend.Contracts.Auth;

namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class AuthTests(ApiFixture fixture)
{
    // Both login failure paths must answer with this same message. Anything that
    // distinguishes "no such account" from "wrong password" is user enumeration.
    private const string LoginFailureMessage = "Invalid email or password";

    // ------------------------------------------------------------------- login

    [Fact] // AUTH-05
    public async Task Login_WithCorrectCredentials_ReturnsToken()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (_, user) = await fixture.RegisterAsync(ct);
        var client = fixture.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            Email = user.Email,
            Password = ApiExtensions.ValidPassword,
        }, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var body = await response.Content.ReadFromJsonAsync<LoginUserResponse>(ct);
        body.ShouldNotBeNull();
        body.Id.ShouldBe(user.Id);
        body.Token.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact] // AUTH-06
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (_, user) = await fixture.RegisterAsync(ct);
        var client = fixture.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            Email = user.Email,
            Password = ApiExtensions.ValidPassword + "wrong",
        }, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid_credentials", ct);
        (await response.Content.ReadAsStringAsync(ct)).ShouldContain(LoginFailureMessage);
    }

    [Fact] // AUTH-07
    public async Task Login_WithUnknownEmail_ReturnsUnauthorizedWithTheSameMessage()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            Email = ApiExtensions.UniqueEmail(),
            Password = ApiExtensions.ValidPassword,
        }, ct);

        // Assert
        // Same status and same message as AUTH-06: an attacker cannot tell an
        // unregistered address from a wrong password.
        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid_credentials", ct);
        (await response.Content.ReadAsStringAsync(ct)).ShouldContain(LoginFailureMessage);
    }

    // ------------------------------------------------------------------ google

    [Fact] // AUTH-12
    public async Task GoogleLogin_WithGarbageToken_ReturnsGoogleTokenInvalid()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();

        // Act
        // Not a fake token, so it goes through the real GoogleJsonWebSignature check.
        var response = await client.GoogleLoginAsync("not-a-google-token", ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "google_token_invalid", ct);
    }

    [Fact] // AUTH-13
    public async Task GoogleLogin_ForUnknownEmail_RegistersUserAndReturnsToken()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();
        var email = ApiExtensions.UniqueEmail();

        // Act
        var response = await client.GoogleLoginAsync(ApiExtensions.GoogleToken(email), ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var body = await response.Content.ReadFromJsonAsync<LoginUserResponse>(ct);
        body.ShouldNotBeNull();
        body.Id.ShouldNotBe(Guid.Empty);
        body.Email.ShouldBe(email);
        body.FirstName.ShouldBe("Grace");
        body.LastName.ShouldBe("Hopper");
        body.Token.ShouldNotBeNullOrWhiteSpace();

        var authenticated = fixture.CreateClient();
        authenticated.UseBearer(body.Token);
        (await authenticated.GetAsync("/api/UserConnection", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact] // AUTH-14
    public async Task GoogleLogin_SecondTimeWithSameSubject_ReturnsSameUserAndMember()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();
        var token = ApiExtensions.GoogleToken(ApiExtensions.UniqueEmail());
        var first = await (await client.GoogleLoginAsync(token, ct)).ReadOkAsync<LoginUserResponse>(ct);
        var firstClient = fixture.CreateClient();
        firstClient.UseBearer(first.Token);
        var group = await firstClient.CreateGroupAsync("Google", ct);
        var firstMemberId = await firstClient.MyMemberIdAsync(group.Id, ct);

        // Act
        var response = await client.GoogleLoginAsync(token, ct);

        // Assert
        var second = await response.ReadOkAsync<LoginUserResponse>(ct);
        second.Id.ShouldBe(first.Id);

        var secondClient = fixture.CreateClient();
        secondClient.UseBearer(second.Token);
        (await secondClient.MyMemberIdAsync(group.Id, ct)).ShouldBe(firstMemberId);
    }

    [Fact] // AUTH-15
    public async Task GoogleLogin_ForEmailRegisteredWithPassword_LinksToExistingAccount()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (_, user) = await fixture.RegisterAsync(ct, firstName: "Local", lastName: "Account");
        var client = fixture.CreateClient();

        // Act
        var response = await client.GoogleLoginAsync(ApiExtensions.GoogleToken(user.Email), ct);

        // Assert
        var body = await response.ReadOkAsync<LoginUserResponse>(ct);
        body.Id.ShouldBe(user.Id);
        // Linking must not overwrite the names the user registered with.
        body.FirstName.ShouldBe("Local");
        body.LastName.ShouldBe("Account");
    }

    [Fact] // AUTH-16
    public async Task GoogleLogin_WithUnverifiedEmail_ReturnsGoogleEmailUnverified()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();
        var token = ApiExtensions.GoogleToken(ApiExtensions.UniqueEmail(), emailVerified: false);

        // Act
        var response = await client.GoogleLoginAsync(token, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "google_email_unverified", ct);
    }

    [Fact] // AUTH-17
    public async Task GoogleLogin_WithoutEmail_ReturnsGoogleEmailMissing()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();
        var token = ApiExtensions.GoogleToken(email: "");

        // Act
        var response = await client.GoogleLoginAsync(token, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "google_email_missing", ct);
    }

    [Fact] // AUTH-18
    public async Task Login_WithPasswordForGoogleAccount_ReturnsUnauthorized()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();
        var email = ApiExtensions.UniqueEmail();
        await (await client.GoogleLoginAsync(ApiExtensions.GoogleToken(email), ct))
            .ReadOkAsync<LoginUserResponse>(ct);

        // Act
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            Email = email,
            Password = ApiExtensions.ValidPassword,
        }, ct);

        // Assert
        // Google accounts have no password hash; same answer as AUTH-06.
        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid_credentials", ct);
        (await response.Content.ReadAsStringAsync(ct)).ShouldContain(LoginFailureMessage);
    }

    [Theory] // AUTH-19
    [InlineData(null, null, "Ada Lovelace", "Ada", "Lovelace")]
    [InlineData(null, null, "Cher", "Cher", "")]
    [InlineData("  Grace ", null, "Grace Brewster Hopper", "Grace", "Brewster Hopper")]
    public async Task GoogleLogin_WithIncompleteNameClaims_ResolvesNames(
        string? givenName, string? familyName, string? name, string expectedFirst, string expectedLast)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();
        var token = ApiExtensions.GoogleToken(
            ApiExtensions.UniqueEmail(), givenName: givenName, familyName: familyName, name: name);

        // Act
        var response = await client.GoogleLoginAsync(token, ct);

        // Assert
        var body = await response.ReadOkAsync<LoginUserResponse>(ct);
        body.FirstName.ShouldBe(expectedFirst);
        body.LastName.ShouldBe(expectedLast);
    }

    [Fact] // AUTH-19
    public async Task GoogleLogin_WithNoNameClaims_FallsBackToEmailLocalPart()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();
        var email = ApiExtensions.UniqueEmail();
        var token = ApiExtensions.GoogleToken(email, givenName: null, familyName: null, name: null);

        // Act
        var response = await client.GoogleLoginAsync(token, ct);

        // Assert
        var body = await response.ReadOkAsync<LoginUserResponse>(ct);
        body.FirstName.ShouldBe(email.Split('@')[0]);
        body.LastName.ShouldBe("");
    }

    [Fact] // AUTH-20
    public async Task GoogleLogin_WithoutIdToken_ReturnsValidationError()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Auth/google", new { }, ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_error", ct);
    }

    // ----------------------------------------------------------- token handling

    [Fact] // AUTH-11
    public async Task AuthenticatedRoute_WithTokenFromRegister_ReturnsOk()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.RegisterAsync(ct);

        // Act
        var response = await client.GetAsync("/api/UserConnection", ct);

        // Assert
        // Proves the issuer, audience and signing key the app validates with are
        // the same ones TokenService signs with.
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
    }

    [Fact] // AUTH-09
    public async Task AuthenticatedRoute_WithTamperedSignature_ReturnsUnauthorized()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (_, user) = await fixture.RegisterAsync(ct);
        var client = fixture.CreateClient();
        client.UseBearer(TamperSignature(user.Token));

        // Act
        var response = await client.GetAsync("/api/UserConnection", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact] // AUTH-10
    public async Task AuthenticatedRoute_WithExpiredToken_ReturnsUnauthorized()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (_, user) = await fixture.RegisterAsync(ct);
        var client = fixture.CreateClient();
        // Minted rather than obtained, because expiry cannot be reached any other
        // way - TokenService issues 7-day tokens. Backdated well past the 2-minute
        // ClockSkew the app allows.
        client.UseBearer(CreateExpiredToken(user.Id, user.Email));

        // Act
        var response = await client.GetAsync("/api/UserConnection", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ helpers

    private static string TamperSignature(string token)
    {
        var parts = token.Split('.');
        parts.Length.ShouldBe(3, "a JWT should have three segments");

        var signature = parts[2];
        parts[2] = (signature[0] == 'A' ? 'B' : 'A') + signature[1..];

        return string.Join('.', parts);
    }

    private static string CreateExpiredToken(Guid userId, string email)
    {
        var key = new SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(ApiFixture.JwtSigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
            ]),
            NotBefore = DateTime.UtcNow.AddHours(-2),
            Expires = DateTime.UtcNow.AddHours(-1),
            Issuer = ApiFixture.JwtIssuer,
            Audience = ApiFixture.JwtAudience,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature),
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
