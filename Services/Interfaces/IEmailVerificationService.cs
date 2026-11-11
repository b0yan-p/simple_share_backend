using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Services;

/// <summary>
/// Owns the email-confirmation half of registration: minting the Identity
/// token, turning it into a link, and spending it again on the way back.
/// </summary>
public interface IEmailVerificationService
{
    /// <summary>
    /// Mails the user a fresh confirmation link. Throws whatever the mail
    /// transport throws - the caller decides what a failed send means.
    /// </summary>
    Task SendVerificationLinkAsync(User user, CancellationToken ct = default);

    /// <summary>
    /// Sends the link again for an address that is still waiting on one. Does
    /// nothing, silently, for an unknown or already confirmed address, and for
    /// a repeat inside the cooldown - the caller cannot tell those apart.
    /// </summary>
    Task ResendAsync(string email, CancellationToken ct = default);

    /// <summary>Throws invalid_token (400) for an unknown user or a token Identity rejects.</summary>
    Task<ConfirmEmailResponse> ConfirmAsync(ConfirmEmailRequest input);
}
