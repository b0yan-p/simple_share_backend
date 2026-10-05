using Microsoft.Extensions.Options;
using Resend;
using simpleShare_backend.Common.Email;
using simpleShare_backend.Services.Interfaces;

namespace simpleShare_backend.Services;

/// <summary>
/// The only class in the application that knows emails go out through Resend.
/// Subjects and HTML are owned by the templates published on the Resend
/// dashboard; this service only names a template and fills its variables.
/// </summary>
public class ResendEmailService(
    IResend resend,
    IOptions<ResendOptions> options,
    ILogger<ResendEmailService> logger
) : IEmailService
{
    private readonly ResendOptions _options = options.Value;

    public async Task SendVerificationEmailAsync(
        string email,
        string firstName,
        string verificationUrl,
        CancellationToken cancellationToken = default
    )
    {
        var message = new EmailMessage
        {
            From = new EmailAddress
            {
                Email = _options.FromEmail,
                DisplayName = _options.FromName,
            },
            To = email,
            // Setting Template is mutually exclusive with Subject/HtmlBody -
            // both come from the published template.
            Template = new EmailMessageTemplate
            {
                TemplateId = EmailTemplates.Verification,
                Variables = new Dictionary<string, object>
                {
                    ["FIRST_NAME"] = firstName,
                    ["VERIFICATION_URL"] = verificationUrl,
                },
            },
        };

        try
        {
            // The client is configured to throw, so a non-success response never
            // reaches this line silently.
            var response = await resend.EmailSendAsync(message, cancellationToken);

            logger.LogInformation(
                "Sent verification email to {Email} using template {Template} (resend id {EmailId})",
                email,
                EmailTemplates.Verification,
                response.Content
            );
        }
        catch (ResendException ex)
        {
            // Deliberately no retry/queue here: the caller decides what a failed
            // send means. The verification URL is never logged - it carries the
            // token.
            logger.LogError(
                ex,
                "Failed to send verification email to {Email} using template {Template} (resend error {ErrorType})",
                email,
                EmailTemplates.Verification,
                ex.ErrorType
            );
            throw;
        }
    }
}
