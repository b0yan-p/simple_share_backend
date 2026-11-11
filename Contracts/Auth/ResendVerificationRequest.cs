using System.ComponentModel.DataAnnotations;

namespace simpleShare_backend.Contracts.Auth
{
    public class ResendVerificationRequest
    {
        [Required]
        [EmailAddress]
        public required string Email { get; set; }
    }
}
