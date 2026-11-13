using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Models.Domain;

public class BetaSignup
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public string Email { get; set; } = string.Empty;
  public string NormalizedEmail { get; set; } = string.Empty;
  public BetaSignupStatus Status { get; set; } = BetaSignupStatus.Waitlisted;
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime? InvitedAt { get; set; }
}
