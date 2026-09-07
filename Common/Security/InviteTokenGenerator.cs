using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace simpleShare_backend.Common.Security;

public static class InviteTokenGenerator
{
    /// <summary>
    /// 16 bytes = 128 bits, the same entropy as a GUID, which is far past
    /// guessable for a link that can also be revoked. Base64url makes that 22
    /// characters instead of the 43 a 32-byte token would need — a shorter link
    /// and a visibly less dense QR code.
    /// </summary>
    public static string Generate() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
}
