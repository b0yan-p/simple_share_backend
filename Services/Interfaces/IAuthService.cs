using simpleShare_backend.Contracts.Auth;

namespace simpleShare_backend.Services;

/// <summary>The sign-in use cases. Every one answers with the same LoginUserResponse.</summary>
public interface IAuthService
{
    /// <summary>Throws passwords_mismatch or registration_failed (400).</summary>
    Task<LoginUserResponse> RegisterAsync(RegisterRequest input);

    /// <summary>Throws invalid_credentials (401) - the same for an unknown email and a wrong password.</summary>
    Task<LoginUserResponse> LoginAsync(LoginRequest input);

    /// <summary>
    /// Login and registration in one: finds the account by Google `sub`, else
    /// links Google to the account with the same email, else creates one.
    /// </summary>
    Task<LoginUserResponse> GoogleLoginAsync(string idToken, CancellationToken ct = default);
}
