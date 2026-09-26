using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Services;

public interface IAuthSessionService
{
    /// <summary>
    /// Issues a token for an already authenticated user and returns it with the
    /// user's details - the response every sign-in endpoint answers with.
    /// </summary>
    Task<LoginUserResponse> CreateSessionAsync(User user);
}
