using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using TokenOptions = Microsoft.Extensions.Options.Options;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Common.Security;

public sealed class PasswordResetTokenProvider(
    IDataProtectionProvider protection,
    ILogger<DataProtectorTokenProvider<User>> logger)
    : DataProtectorTokenProvider<User>(
        protection,
        TokenOptions.Create(new DataProtectionTokenProviderOptions
        {
            Name = ProviderName,
            TokenLifespan = TimeSpan.FromMinutes(ExpirationMinutes)
        }),
        logger)
{
    public const string ProviderName = "PasswordReset";
    public const int ExpirationMinutes = 30;
}
