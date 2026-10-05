namespace simpleShare_backend.Services.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(
        string email,
        string firstName,
        string verificationUrl,
        CancellationToken cancellationToken = default
    );
}
