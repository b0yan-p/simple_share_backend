
using System.ComponentModel.DataAnnotations;

public class JoinBetaRequest
{
  [Required]
  [EmailAddress]
  [MaxLength(254)]
  public string Email { get; set; } = string.Empty;

  // Honeypot field for bot detection.
  public string? Name { get; set; }
}
