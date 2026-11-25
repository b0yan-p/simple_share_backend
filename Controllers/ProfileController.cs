using Microsoft.AspNetCore.Identity;
using simpleShare_backend.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Contracts.Profile;
using simpleShare_backend.Data;

namespace simpleShare_backend.Controllers;

[ApiController]
[Authorize]
[Route("api/profile/me")]
public class ProfileController(ApplicationDbContext db, UserManager<User> users) : ControllerBase
{
    [HttpDelete]
    public async Task<IActionResult> DeleteAsync([FromServices] simpleShare_backend.Services.AccountDeletionService accounts, CancellationToken ct)
    {
        await accounts.DeleteAsync(User.GetUserId(), ct);
        return NoContent();
    }

    [HttpGet]
    public async Task<ProfileResponse> GetAsync(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var profile = await db.Members.Where(m => m.UserId == userId && m.DeletedAt == null)
            .Select(m => new { m.User, m.DisplayName })
            .SingleOrDefaultAsync(ct) ?? throw ProfileNotFound();
        return await ToResponseAsync(profile.User!, profile.DisplayName);
    }

    [HttpPut]
    public async Task<ProfileResponse> UpdateAsync(ProfileDetails input, CancellationToken ct)
    {
        var firstName = input.FirstName.Trim();
        var lastName = input.LastName.Trim();
        var displayName = input.DisplayName.Trim();

        if (firstName.Length == 0 || displayName.Length is < 2 or > 80)
        {
            throw new ValidationAppException(
                "invalid_profile",
                "Enter a first name and a display name between 2 and 80 characters."
            );
        }

        var userId = User.GetUserId();
        var member = await db.Members.AsTracking().Include(m => m.User)
            .SingleOrDefaultAsync(m => m.UserId == userId && m.DeletedAt == null, ct)
            ?? throw ProfileNotFound();

        member.User!.FirstName = firstName;
        member.User.LastName = lastName;
        member.FirstName = firstName;
        member.LastName = lastName;
        member.DisplayName = displayName;
        member.ModifiedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return await ToResponseAsync(member.User, displayName);
    }

    private async Task<ProfileResponse> ToResponseAsync(User user, string displayName) =>
        new(user.FirstName, user.LastName, displayName, await users.HasPasswordAsync(user));

    private static NotFoundAppException ProfileNotFound() => new("profile_not_found", "Profile not found.");
}
