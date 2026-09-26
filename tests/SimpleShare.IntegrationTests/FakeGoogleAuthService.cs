using System.Text;
using System.Text.Json;
using Google.Apis.Auth;
using Microsoft.AspNetCore.WebUtilities;
using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Services;

namespace SimpleShare.IntegrationTests;

/// <summary>
/// Stands in for Google's signature check, which needs a real Google-issued
/// token that CI cannot get. Tokens made by <see cref="CreateToken"/> skip only
/// the signature step; the email checks and name resolution still run through
/// <see cref="GoogleAuthService.ToExternalIdentity"/>. Any other token goes to the real
/// service, so a garbage token gets the real `google_token_invalid` answer.
/// </summary>
public sealed class FakeGoogleAuthService(GoogleAuthService real) : IGoogleAuthService
{
    private const string Prefix = "test-google:";

    public Task<ExternalIdentity> ValidateIdTokenAsync(string idToken, CancellationToken ct = default)
    {
        if (!idToken.StartsWith(Prefix, StringComparison.Ordinal))
            return real.ValidateIdTokenAsync(idToken, ct);

        var json = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(idToken[Prefix.Length..]));
        var payload = JsonSerializer.Deserialize<GoogleJsonWebSignature.Payload>(json)
            ?? throw new InvalidOperationException("empty fake Google payload");

        return Task.FromResult(GoogleAuthService.ToExternalIdentity(payload));
    }

    public static string CreateToken(GoogleJsonWebSignature.Payload payload) =>
        Prefix + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
}
