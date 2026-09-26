using simpleShare_backend.Contracts.Auth;

namespace simpleShare_backend.Services;

public interface IGoogleAuthService
{
    /// <summary>
    /// Validates a Google idToken and returns the identity behind it. Throws
    /// ValidationAppException (google_token_invalid, google_email_missing,
    /// google_email_unverified) when the token cannot be trusted.
    /// </summary>
    Task<ExternalIdentity> ValidateIdTokenAsync(string idToken, CancellationToken ct = default);
}
