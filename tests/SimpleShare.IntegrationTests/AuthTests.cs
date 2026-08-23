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

    // ---------------------------------------------------------------- register

    [Fact] // AUTH-01
    public async Task Register_WithValidPayload_ReturnsUserAndToken()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();
        var email = ApiExtensions.UniqueEmail();
        var request = new
        {
            Email = email,
            Password = ApiExtensions.ValidPassword,
            RepeatPassword = ApiExtensions.ValidPassword,
            FirstName = "Ada",
            LastName = "Lovelace",
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/Auth/register", request, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var body = await response.Content.ReadFromJsonAsync<LoginUserResponse>(ct);
        body.ShouldNotBeNull();
        body.Id.ShouldNotBe(Guid.Empty);
        body.Email.ShouldBe(email);
        body.FirstName.ShouldBe("Ada");
        body.LastName.ShouldBe("Lovelace");
        body.Token.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact] // AUTH-02
    public async Task Register_WithEmailAlreadyTaken_ReturnsBadRequest()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();
        var email = ApiExtensions.UniqueEmail();
        var request = new
        {
            Email = email,
            Password = ApiExtensions.ValidPassword,
            RepeatPassword = ApiExtensions.ValidPassword,
            FirstName = "First",
            LastName = "Owner",
        };
        (await client.PostAsJsonAsync("/api/Auth/register", request, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Act
        var response = await client.PostAsJsonAsync("/api/Auth/register", request, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory] // AUTH-03
    [InlineData("password1", "no uppercase")]
    [InlineData("PASSWORD1", "no lowercase")]
    [InlineData("Password", "no digit")]
    [InlineData("Pw1", "shorter than 6")]
    public async Task Register_WithPasswordBelowPolicy_ReturnsBadRequest(string password, string reason)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();
        var request = new
        {
            Email = ApiExtensions.UniqueEmail(),
            Password = password,
            RepeatPassword = password,
            FirstName = "Test",
            LastName = "User",
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/Auth/register", request, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"password rejected for: {reason}");
    }

    [Fact] // AUTH-04
    public async Task Register_WithMismatchedRepeatPassword_ReturnsBadRequest()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();
        var request = new
        {
            Email = ApiExtensions.UniqueEmail(),
            Password = ApiExtensions.ValidPassword,
            RepeatPassword = ApiExtensions.ValidPassword + "x",
            FirstName = "Test",
            LastName = "User",
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/Auth/register", request, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

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
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
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
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(ct)).ShouldContain(LoginFailureMessage);
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
