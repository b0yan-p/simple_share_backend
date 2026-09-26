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
    IExternalAccountService externalAccountService
) : IAuthService
{
    // One message for both login failures. Anything that distinguishes "no such
    // account" from "wrong password" is user enumeration.
    private const string InvalidCredentialsMessage = "Invalid email or password";

    public async Task<LoginUserResponse> RegisterAsync(RegisterRequest input)
    {
        if (input.Password != input.RepeatPassword)
            throw new ValidationAppException("passwords_mismatch", "Passwords do not match");

        var user = new User
        {
            FirstName = input.FirstName,
            LastName = input.LastName,
            Email = input.Email,
            UserName = input.Email
        };

        var createUser = await userManager.CreateAsync(user, input.Password);
        if (!createUser.Succeeded)
            throw new ValidationAppException(
                "registration_failed", "Could not create account", createUser.ToErrorDictionary());

        (await userManager.AddToRoleAsync(user, AppRoles.User)).EnsureSucceeded("AddToRole");
        await memberService.EnsureMemberForUserAsync(user);

        return await authSessionService.CreateSessionAsync(user);
    }

    public async Task<LoginUserResponse> LoginAsync(LoginRequest input)
    {
        var user = await userManager.FindByEmailAsync(input.Email);

        // Accounts created through Google have no password, so this is false for them too.
        if (user is null || !await userManager.CheckPasswordAsync(user, input.Password))
            throw new UnauthorizedAppException("invalid_credentials", InvalidCredentialsMessage);

        return await authSessionService.CreateSessionAsync(user);
    }

    public async Task<LoginUserResponse> GoogleLoginAsync(string idToken, CancellationToken ct = default)
    {
        var identity = await googleAuthService.ValidateIdTokenAsync(idToken, ct);
        var user = await externalAccountService.FindOrCreateUserAsync(identity);
        await memberService.EnsureMemberForUserAsync(user);

        return await authSessionService.CreateSessionAsync(user);
    }
}
