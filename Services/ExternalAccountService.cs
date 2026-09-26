using Microsoft.AspNetCore.Identity;
using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Common.Security;
using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Services;

public class ExternalAccountService(UserManager<User> userManager) : IExternalAccountService
{
    public async Task<User> FindOrCreateUserAsync(ExternalIdentity identity)
    {
        var user = await userManager.FindByLoginAsync(identity.Provider, identity.ProviderKey)
            ?? await LinkOrCreateAsync(identity);

        // Checked on every login rather than only after create, so an account
        // left half-created by an earlier failed attempt heals itself.
        if (!await userManager.IsInRoleAsync(user, AppRoles.User))
            (await userManager.AddToRoleAsync(user, AppRoles.User)).EnsureSucceeded("AddToRole");

        return user;
    }

    private async Task<User> LinkOrCreateAsync(ExternalIdentity identity)
    {
        var user = await userManager.FindByEmailAsync(identity.Email);

        if (user is null)
            user = await CreateAsync(identity);
        else
            await ConfirmEmailAsync(user);

        var login = new UserLoginInfo(identity.Provider, identity.ProviderKey, identity.Provider);
        (await userManager.AddLoginAsync(user, login)).EnsureSucceeded("AddLogin");

        return user;
    }

    private async Task<User> CreateAsync(ExternalIdentity identity)
    {
        var user = new User
        {
            FirstName = identity.FirstName,
            LastName = identity.LastName,
            Email = identity.Email,
            UserName = identity.Email,
            EmailConfirmed = true
        };

        // No password: external accounts sign in through their provider only.
        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
            throw new ValidationAppException(
                "external_account_create_failed", "Could not create account", result.ToErrorDictionary());

        return user;
    }

    // The provider has verified the address, so the local account may trust it too.
    private async Task ConfirmEmailAsync(User user)
    {
        if (user.EmailConfirmed)
            return;

        user.EmailConfirmed = true;
        (await userManager.UpdateAsync(user)).EnsureSucceeded("Update");
    }
}
