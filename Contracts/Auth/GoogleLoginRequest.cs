using System.ComponentModel.DataAnnotations;

namespace simpleShare_backend.Contracts.Auth
{
    public class GoogleLoginRequest
    {
        [Required]
        public required string IdToken { get; set; }
    }
}
