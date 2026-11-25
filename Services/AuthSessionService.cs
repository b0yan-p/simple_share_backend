using Microsoft.AspNetCore.Identity;
using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Interfaces;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Services;

public class AuthSessionService(UserManager<User> userManager, ITokenService tokenService, IMemberService memberService) : IAuthSessionService
{
    public async Task<LoginUserResponse> CreateSessionAsync(User user)
    {
        var member = await memberService.EnsureMemberForUserAsync(user);
        var roles = await userManager.GetRolesAsync(user);
        return new LoginUserResponse
        {
            Id = user.Id,
            Email = user.Email!,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = member.DisplayName,
            Token = tokenService.CreateToken(user, roles)
        };
    }
}
