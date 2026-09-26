using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Common.Security;
using simpleShare_backend.Contracts.Auth;

namespace simpleShare_backend.Services;

public class GoogleAuthService(IOptions<GoogleAuthOptions> options) : IGoogleAuthService
{
    public const string ProviderName = "Google";

    // Member.DisplayName is "{FirstName} {LastName}" capped at 80 characters.
    private const int MaxNameLength = 40;

    private readonly GoogleAuthOptions _options = options.Value;

    public async Task<ExternalIdentity> ValidateIdTokenAsync(string idToken, CancellationToken ct = default)
    {
        var audiences = _options.AllowedAudiences
            .Append(_options.ClientId)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct()
            .ToArray();

        // Without an audience the library skips the `aud` check entirely, and a
        // token Google issued to any other app would be accepted here.
        if (audiences.Length == 0)
            throw new InvalidOperationException("Google:ClientId is not configured");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = audiences });
        }
        catch (InvalidJwtException)
        {
            // expired, bad signature, wrong aud/iss, or not a JWT at all
            throw new ValidationAppException("google_token_invalid", "Invalid Google token");
        }

        return ToExternalIdentity(payload);
    }

    /// <summary>
    /// Everything after signature validation: the email checks and name
    /// resolution. Public so the integration tests' fake runs the same rules.
    /// </summary>
    public static ExternalIdentity ToExternalIdentity(GoogleJsonWebSignature.Payload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Email))
            throw new ValidationAppException("google_email_missing", "Google account has no email");

        // Hard requirement: the endpoint links accounts by email, so an
        // unverified address would be a way into someone else's account.
        if (!payload.EmailVerified)
            throw new ValidationAppException("google_email_unverified", "Google email is not verified");

        var email = payload.Email.Trim();
        var (firstName, lastName) = ResolveNames(payload.GivenName, payload.FamilyName, payload.Name, email);

        return new ExternalIdentity(ProviderName, payload.Subject, email, firstName, lastName);
    }

    private static (string FirstName, string LastName) ResolveNames(
        string? givenName, string? familyName, string? name, string email)
    {
        var emailLocalPart = email.Split('@')[0];
        var nameParts = (name ?? string.Empty).Split(' ', 2,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var firstName = FirstNonBlank(givenName, nameParts.ElementAtOrDefault(0), emailLocalPart);
        var lastName = FirstNonBlank(familyName, nameParts.ElementAtOrDefault(1), string.Empty);

        firstName = Truncate(firstName);
        lastName = Truncate(lastName);

        if ($"{firstName} {lastName}".Trim().Length < 2)
            firstName = Truncate(emailLocalPart);

        return (firstName, lastName);
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string Truncate(string value) =>
        value.Length > MaxNameLength ? value[..MaxNameLength].TrimEnd() : value;
}
