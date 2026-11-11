using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using simpleShare_backend.Common.Endpoints;
using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Models.Identity;
using simpleShare_backend.Services.Interfaces;

namespace simpleShare_backend.Services;

public class EmailVerificationService(
    UserManager<User> userManager,
    IEmailService emailService,
    IOptions<AppLinkOptions> appLinkOptions,
    IMemoryCache cache,
    ILogger<EmailVerificationService> logger
) : IEmailVerificationService
{
    /// <summary>
    /// Path of the page that collects the confirmation click. The same value is
    /// claimed by the Android intent-filter and by the Apple association file,
    /// so changing it here means changing it in all three places.
    /// </summary>
    public const string VerificationPath = "/verify-email";

    // Identity rejects a token whose surrounding state changed, which is what a
    // second confirmation looks like. Telling those apart from a truly bad link
    // is done by re-reading EmailConfirmed, not by inspecting the error.
    private const string InvalidTokenMessage =
        "This link is not valid or has expired. Register again to get a new one.";

    /// <summary>
    /// How long one account has to wait between resends. The endpoint is
    /// anonymous, so without this anyone knowing an address could use it to
    /// mail-bomb that inbox from our domain. In memory rather than a column:
    /// the deployment is a single container, and a cooldown lost on restart is
    /// not worth a schema change.
    /// </summary>
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(1);

    private readonly AppLinkOptions _appLinks = appLinkOptions.Value;

    public async Task SendVerificationLinkAsync(User user, CancellationToken ct = default)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

        await emailService.SendVerificationEmailAsync(
            user.Email!,
            user.FirstName,
            BuildVerificationUrl(user.Id, token),
            ct
        );
    }

    public async Task ResendAsync(string email, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(email);

        // Nothing here changes what the caller sees. A resend form that answers
        // differently for a registered address is an enumeration oracle, and
        // accounts created through Google are already confirmed, so they fall
        // out here too - they have no password to sign in with anyway.
        if (user is null || user.EmailConfirmed)
            return;

        var cooldownKey = $"resend-verification:{user.Id}";
        if (cache.TryGetValue(cooldownKey, out _))
        {
            logger.LogInformation("Verification resend for user {UserId} skipped: cooling down", user.Id);
            return;
        }

        try
        {
            await SendVerificationLinkAsync(user, ct);
        }
        catch (Exception ex)
        {
            // A transport failure is the one thing this endpoint does report,
            // because it is the one thing retrying can fix. It says nothing
            // about whether the address is registered: an unknown one returned
            // long before reaching here.
            logger.LogError(ex, "Could not resend the verification email for user {UserId}", user.Id);

            throw new ValidationAppException(
                "verification_email_failed",
                "Could not send the email right now. Try again in a moment.");
        }

        // Only once something actually left: a send that failed put nothing in
        // the inbox, so making the user wait a minute to retry it would be a
        // cooldown against nothing - and the next attempt would answer "sent".
        cache.Set(cooldownKey, true, ResendCooldown);
    }

    public async Task<ConfirmEmailResponse> ConfirmAsync(ConfirmEmailRequest input)
    {
        var user = await userManager.FindByIdAsync(input.UserId.ToString())
            ?? throw new ValidationAppException("invalid_token", InvalidTokenMessage);

        if (user.EmailConfirmed)
            return new ConfirmEmailResponse { Confirmed = true, AlreadyConfirmed = true };

        var result = await userManager.ConfirmEmailAsync(user, Decode(input.Token));
        if (!result.Succeeded)
        {
            // No token and no URL in the log line - both are credentials until spent.
            logger.LogWarning(
                "Email confirmation rejected for user {UserId}: {Errors}",
                user.Id,
                string.Join(", ", result.Errors.Select(e => e.Code))
            );

            throw new ValidationAppException("invalid_token", InvalidTokenMessage);
        }

        logger.LogInformation("Email confirmed for user {UserId}", user.Id);

        return new ConfirmEmailResponse { Confirmed = true };
    }

    /// <summary>
    /// The raw Identity token is Base64 with '+', '/' and '=' in it, none of
    /// which survive a round trip through a query string intact - so it travels
    /// Base64Url-encoded and is decoded again in <see cref="Decode"/>.
    /// </summary>
    private string BuildVerificationUrl(Guid userId, string token)
    {
        var encoded = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));

        return QueryHelpers.AddQueryString(
            $"{_appLinks.PublicBaseUrl.TrimEnd('/')}{VerificationPath}",
            new Dictionary<string, string?> { ["userId"] = userId.ToString(), ["token"] = encoded }
        );
    }

    private static string Decode(string token)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            // A hand-edited or truncated link, answered the same as a wrong one.
            throw new ValidationAppException("invalid_token", InvalidTokenMessage);
        }
    }
}
