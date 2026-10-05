using simpleShare_backend.Services.Interfaces;

namespace simpleShare_backend.Common.Endpoints;

/// <summary>
/// Manual smoke test for the Resend integration. Mapped only when the host runs
/// in Development, so the route simply does not exist in production - nothing
/// to abuse into sending arbitrary mail.
/// </summary>
public static class DevEmailEndpoints
{
    private const string TestFirstName = "Bojan";
    private const string TestVerificationUrl = "https://shareorb.app/verify-email?token=test";

    public static void MapDevEmailEndpoints(this WebApplication app)
    {
        app.MapPost(
                "/api/dev/email/verification",
                async (DevVerificationEmailRequest request, IEmailService emailService, CancellationToken ct) =>
                {
                    await emailService.SendVerificationEmailAsync(
                        request.Email,
                        TestFirstName,
                        TestVerificationUrl,
                        ct
                    );

                    return Results.Ok(new { sent = true, to = request.Email });
                }
            )
            .AllowAnonymous();
    }

    public record DevVerificationEmailRequest(string Email);
}
