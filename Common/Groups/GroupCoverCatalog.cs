using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Common.Groups;

public static class GroupCoverCatalog
{
    private static readonly Dictionary<GroupType, string[]> KeysByType = new()
    {
        [GroupType.Trip] =
        [
            "alicante-sunset", "banja-luka-sunset", "paris-sunset",
            "santorini-sunset", "seville-sunset", "tokyo-sunset",
        ],
        [GroupType.Roommates] = ["roommate-1", "roommate-2", "roommate-3", "roommate-4", "roommate-5"],
        [GroupType.Couple] = ["couple-1", "couple-2", "couple-3", "couple-4", "couple-5"],
        [GroupType.Other] = ["other-1", "other-2", "other-3", "other-4", "other-5"],
    };

    public static string DefaultFor(GroupType type) => KeysByType[type][0];

    public static string Validate(GroupType type, string key)
    {
        if (!KeysByType[type].Contains(key, StringComparer.Ordinal))
            throw new ValidationAppException("invalid_group_cover", "Select a cover image for this group type.");

        return key;
    }
}
