using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using simpleShare_backend.Common.Endpoints;
using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Models.Identity;
using simpleShare_backend.Services.Interfaces;

namespace simpleShare_backend.Services;

public sealed class PasswordRecoveryService(
    UserManager<User> users,
    IEmailService emailService,
    IOptions<AppLinkOptions> appLinks,
    IMemoryCache cache,
    ILogger<PasswordRecoveryService> logger)
{
    private static readonly Lock CooldownLock = new();
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);

    public async Task SendLinkAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null || !user.EmailConfirmed || !await users.HasPasswordAsync(user))
            return;

        var key = $"forgot-password:{user.Id}";
        lock (CooldownLock)
        {
            if (cache.TryGetValue(key, out _))
                return;

            cache.Set(key, true, Cooldown);
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var url = $"{appLinks.Value.PublicBaseUrl.TrimEnd('/')}/reset-password#userId={user.Id}&token={encodedToken}";

        try
        {
            await emailService.SendPasswordResetEmailAsync(user.Email!, user.FirstName, url, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Password recovery delivery failed for user {UserId} ({ErrorType})",
                user.Id, ex.GetType().Name);
        }
    }

    public async Task ResetAsync(Guid userId, string encodedToken, string newPassword)
    {
        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
        }
        catch (FormatException)
        {
            throw InvalidToken();
        }

        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.EmailConfirmed || !await users.HasPasswordAsync(user))
            throw InvalidToken();

        var result = await users.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded) return;

        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.InvalidToken)
            or nameof(IdentityErrorDescriber.ConcurrencyFailure)))
            throw InvalidToken();

        if (result.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal)))
            throw new ValidationAppException("invalid_new_password", "Choose a stronger password.", result.ToErrorDictionary());

        result.EnsureSucceeded("Reset password");
    }

    private static ValidationAppException InvalidToken() =>
        new("invalid_reset_token", "This reset link is invalid, expired, or already used.");
}
