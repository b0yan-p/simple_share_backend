using Microsoft.AspNetCore.Identity;
using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Common.Security;
using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Services;

public class AuthService(
    UserManager<User> userManager,
    IMemberService memberService,
    IAuthSessionService authSessionService,
    IGoogleAuthService googleAuthService,
    IExternalAccountService externalAccountService,
    IEmailVerificationService emailVerificationService,
    ILogger<AuthService> logger
) : IAuthService
{
    // One message for both login failures. Anything that distinguishes "no such
    // account" from "wrong password" is user enumeration.
    private const string InvalidCredentialsMessage = "Invalid email or password";

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest input)
    {
        if (input.Password != input.RepeatPassword)
            throw new ValidationAppException("passwords_mismatch", "Passwords do not match");

        var existing = await userManager.FindByEmailAsync(input.Email);

        // An unconfirmed account is one that was never usable, so a second
        // attempt means "send it again" - deliberately without touching the
        // password or the profile already on file. A confirmed one falls
        // through to CreateAsync and gets Identity's DuplicateEmail.
        if (existing is not null && !existing.EmailConfirmed)
        {
            await SendVerificationAsync(existing);
            return Sent(existing);
        }

        var user = new User
        {
            FirstName = input.FirstName,
            LastName = input.LastName,
            Email = input.Email,
            UserName = input.Email,
            // Identity's default, spelled out because the whole flow hangs off it.
            EmailConfirmed = false
        };

        var createUser = await userManager.CreateAsync(user, input.Password);
        if (!createUser.Succeeded)
            throw new ValidationAppException(
                "registration_failed", "Could not create account", createUser.ToErrorDictionary());

        (await userManager.AddToRoleAsync(user, AppRoles.User)).EnsureSucceeded("AddToRole");
        await memberService.EnsureMemberForUserAsync(user);

        await SendVerificationAsync(user);

        return Sent(user);
    }

    public async Task<LoginUserResponse> LoginAsync(LoginRequest input)
    {
        var user = await userManager.FindByEmailAsync(input.Email);

        // Accounts created through Google have no password, so this is false for them too.
        if (user is null || !await userManager.CheckPasswordAsync(user, input.Password))
            throw new UnauthorizedAppException("invalid_credentials", InvalidCredentialsMessage);

        // Only after the password checks out: answering this before it would
        // tell an attacker which addresses are registered.
        if (!user.EmailConfirmed)
            throw new ForbiddenAppException(
                "email_not_confirmed",
                "Confirm your email address before signing in. Check your inbox for the link.");

        return await authSessionService.CreateSessionAsync(user);
    }

    public async Task<LoginUserResponse> GoogleLoginAsync(string idToken, CancellationToken ct = default)
    {
        var identity = await googleAuthService.ValidateIdTokenAsync(idToken, ct);
        var user = await externalAccountService.FindOrCreateUserAsync(identity);
        await memberService.EnsureMemberForUserAsync(user);

        return await authSessionService.CreateSessionAsync(user);
    }

    /// <summary>
    /// The account is already saved by the time this runs, and it is kept: a
    /// failed send is a transport problem, not a reason to lose the signup.
    /// Registering again resends, which is why nothing is rolled back here.
    /// </summary>
    private async Task SendVerificationAsync(User user)
    {
        try
        {
            await emailVerificationService.SendVerificationLinkAsync(user);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send the verification email for user {UserId}", user.Id);

            throw new ValidationAppException(
                "verification_email_failed",
                "Your account was created but the confirmation email could not be sent. Try again in a moment.");
        }
    }

    private static RegisterResponse Sent(User user) =>
        new() { Email = user.Email! };
}
