using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Services;

public interface IExternalAccountService
{
    /// <summary>
    /// Returns the user an external identity belongs to: the one already linked
    /// to it, else the one with the same email (linked now), else a new
    /// password-less user. Either way the user ends up in the default role.
    /// The identity must already be verified, email included.
    /// </summary>
    Task<User> FindOrCreateUserAsync(ExternalIdentity identity);
}
