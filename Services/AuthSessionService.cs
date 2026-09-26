using Microsoft.AspNetCore.Identity;
using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Interfaces;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Services;

public class AuthSessionService(UserManager<User> userManager, ITokenService tokenService) : IAuthSessionService
{
    public async Task<LoginUserResponse> CreateSessionAsync(User user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new LoginUserResponse
        {
            Id = user.Id,
            Email = user.Email!,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Token = tokenService.CreateToken(user, roles)
        };
    }
}
