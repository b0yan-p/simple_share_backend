using System.Security.Claims;

namespace simpleShare_backend.Common.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var idStr = user.FindFirstValue(ClaimTypes.NameIdentifier)
             ?? user.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(idStr))
            throw new UnauthorizedAccessException("Missing user id claim.");

        if (!Guid.TryParse(idStr, out var userId))
            throw new UnauthorizedAccessException("Invalid user id claim.");

        return userId;
    }

    public static bool TryGetUserId(this ClaimsPrincipal user, out Guid userId)
    {
        userId = default;

        var idStr = user.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? user.FindFirstValue("sub");

        return !string.IsNullOrWhiteSpace(idStr) && Guid.TryParse(idStr, out userId);
    }
}
