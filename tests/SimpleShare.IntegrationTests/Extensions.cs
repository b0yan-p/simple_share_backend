using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using simpleShare_backend.Contracts.Auth;

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
}
