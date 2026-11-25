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
public class ProfileController(ApplicationDbContext db) : ControllerBase
{
    [HttpDelete]
    public async Task<IActionResult> DeleteAsync([FromServices] simpleShare_backend.Services.AccountDeletionService accounts, CancellationToken ct)
    {
        await accounts.DeleteAsync(User.GetUserId(), ct);
        return NoContent();
    }

    [HttpGet]
    public async Task<ProfileDetails> GetAsync(CancellationToken ct)
    {
        var userId = User.GetUserId();
        return await db.Members.Where(m => m.UserId == userId && m.DeletedAt == null)
            .Select(m => new ProfileDetails(m.User!.FirstName, m.User.LastName, m.DisplayName))
            .SingleOrDefaultAsync(ct) ?? throw ProfileNotFound();
    }

    [HttpPut]
    public async Task<ProfileDetails> UpdateAsync(ProfileDetails input, CancellationToken ct)
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

        return new ProfileDetails(firstName, lastName, displayName);
    }

    private static NotFoundAppException ProfileNotFound() => new("profile_not_found", "Profile not found.");
}
