using simpleShare_backend.Contracts.Auth;

namespace simpleShare_backend.Services;

/// <summary>The sign-in use cases. Only the ones that end in a session answer with LoginUserResponse.</summary>
public interface IAuthService
{
    /// <summary>
    /// Creates the account unconfirmed and mails a confirmation link - no
    /// session, so the answer carries no token. Registering again for an
    /// unconfirmed address resends the link without changing the password or
    /// the profile. Throws passwords_mismatch, registration_failed or
    /// verification_email_failed (400).
    /// </summary>
    Task<RegisterResponse> RegisterAsync(RegisterRequest input);

    /// <summary>
    /// Throws invalid_credentials (401) - the same for an unknown email and a
    /// wrong password - or email_not_confirmed (403) once the password checks out.
    /// </summary>
    Task<LoginUserResponse> LoginAsync(LoginRequest input);

    /// <summary>
    /// Login and registration in one: finds the account by Google `sub`, else
    /// links Google to the account with the same email, else creates one.
    /// Google has already verified the address, so these accounts skip confirmation.
    /// </summary>
    Task<LoginUserResponse> GoogleLoginAsync(string idToken, CancellationToken ct = default);
}
