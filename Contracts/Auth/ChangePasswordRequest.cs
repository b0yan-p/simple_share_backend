using System.ComponentModel.DataAnnotations;

namespace simpleShare_backend.Contracts.Auth;

public sealed record ChangePasswordRequest([Required] string CurrentPassword, [Required] string NewPassword);
