using System.Net;
using Shouldly;

namespace SimpleShare.IntegrationTests;

/// <summary>
/// Table-driven coverage of rule 3: every route that requires a caller identity
/// must answer 401 to an anonymous request. New routes are added here, not as
/// one-off tests.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class AuthorizationMatrixTests(ApiFixture fixture)
{
    // Any well-formed GUID. The request must be rejected before routing ever
    // looks anything up, so it never needs to exist.
    private const string AnyId = "00000000-0000-0000-0000-000000000001";

    public static TheoryData<string, string> AuthenticatedRoutes => new()
    {
        { "GET",    "/api/Group" },
        { "POST",   "/api/Group" },
        { "PUT",    "/api/Group" },
        { "GET",    "/api/Group/recent" },
        { "GET",    $"/api/Group/{AnyId}" },
        { "DELETE", $"/api/Group/{AnyId}" },
        { "POST",   $"/api/Group/{AnyId}/restore" },
        { "GET",    $"/api/Group/{AnyId}/balances" },
        { "GET",    $"/api/Group/{AnyId}/overview" },

        { "POST",   $"/api/GroupMember/{AnyId}/members/bulk" },
        { "DELETE", $"/api/GroupMember/{AnyId}/members/{AnyId}" },
        { "GET",    $"/api/GroupMember/{AnyId}/members" },

        { "POST",   $"/api/Expense/{AnyId}" },
        { "GET",    $"/api/Expense/{AnyId}" },
        { "GET",    $"/api/Expense/{AnyId}/{AnyId}" },
        { "PUT",    $"/api/Expense/{AnyId}" },
        { "DELETE", $"/api/Expense/{AnyId}/{AnyId}" },
        { "POST",   $"/api/Expense/{AnyId}/settleUp" },

        // The preview route is deliberately absent: it is public, so an invited
        // person can see which group they are joining before they have an account.
        { "GET",    $"/api/GroupInvitation/group/{AnyId}" },
        { "POST",   "/api/GroupInvitation/sometoken/join" },

        { "GET",    "/api/UserConnection" },
        { "GET",    "/api/Activity/me" },
        { "GET",    "/api/Balance/me" },
    };

    [Theory] // AUTH-08
    [MemberData(nameof(AuthenticatedRoutes))]
    public async Task AuthenticatedRoute_WithoutToken_ReturnsUnauthorized(string method, string url)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var client = fixture.CreateClient();     // no Authorization header
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        // Act
        var response = await client.SendAsync(request, ct);

        // Assert
        // No body is sent: the authorization middleware rejects before model
        // binding, so a missing body must not change the outcome.
        response.StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized,
            $"{method} {url} answered anonymously");
    }
}
