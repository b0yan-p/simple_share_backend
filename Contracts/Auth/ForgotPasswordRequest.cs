using System.ComponentModel.DataAnnotations;

namespace simpleShare_backend.Contracts.Auth;

public sealed class ForgotPasswordRequest
{
    [EmailAddress]
    [MaxLength(256)]
    public string? Email { get; init; }
    public Guid? UserId { get; init; }

    [MaxLength(2048)]
    public string? Token { get; init; }

    [MaxLength(1024)]
    public string? NewPassword { get; init; }
}
