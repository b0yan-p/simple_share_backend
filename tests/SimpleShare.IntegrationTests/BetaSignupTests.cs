using Microsoft.AspNetCore.Mvc;
using simpleShare_backend.Services;

namespace SimpleShare.IntegrationTests;

public class BetaSignupTests
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData(" \t\r\n", 1)]
    [InlineData("bot", 0)]
    [InlineData(" bot ", 0)]
    public async Task Honeypot_is_checked_before_signup_and_returns_the_same_public_response(
        string? name, int expectedCalls)
    {
        var service = new RecordingBetaSignupService();
        var controller = new BetaSignupController(service);
        var request = new JoinBetaRequest { Email = "person@example.com", Name = name };

        var result = Assert.IsType<OkObjectResult>(
            await controller.JoinBeta(request, CancellationToken.None));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(expectedCalls, service.Calls);
        Assert.Equal(
            "{\"message\":\"Your beta signup request has been received.\"}",
            System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public void Missing_name_defaults_to_null()
    {
        var request = System.Text.Json.JsonSerializer.Deserialize<JoinBetaRequest>(
            "{\"Email\":\"person@example.com\"}");
        Assert.NotNull(request);
        Assert.Null(request.Name);
    }

    private sealed class RecordingBetaSignupService : IBetaSignupService
    {
        public int Calls { get; private set; }

        public Task JoinBetaAsync(string email, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
