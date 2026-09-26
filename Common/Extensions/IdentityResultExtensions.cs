using Microsoft.AspNetCore.Identity;

namespace simpleShare_backend.Common.Extensions;

public static class IdentityResultExtensions
{
    /// <summary>Identity errors as the field-level `errors` a ValidationAppException carries.</summary>
    public static IDictionary<string, string[]> ToErrorDictionary(this IdentityResult result) =>
        result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

    /// <summary>
    /// For Identity calls whose failure is not caused by client input, so it
    /// surfaces as a 500 rather than a validation error.
    /// </summary>
    public static void EnsureSucceeded(this IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"{operation} failed: {string.Join("; ", result.Errors.Select(e => e.Description))}");
    }
}
