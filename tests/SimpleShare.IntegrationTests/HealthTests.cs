using System.Net;
using Shouldly;

namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class HealthTests(ApiFixture fixture)
{
    [Fact] // HLTH-01
    public async Task GetHealth_OnFreshDatabase_ReturnsOk()
    {
        // Arrange
        // First CreateClient() boots the host: EF migrations run against the
        // empty container, then DbSeeder seeds roles and its fixed users.
        var client = fixture.CreateClient();

        // Act
        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
