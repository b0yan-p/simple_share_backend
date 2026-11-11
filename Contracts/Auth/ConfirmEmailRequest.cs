using System.ComponentModel.DataAnnotations;

namespace simpleShare_backend.Contracts.Auth
{
    public class ConfirmEmailRequest
    {
        [Required]
        public required Guid UserId { get; set; }

        /// <summary>The Base64Url form carried by the link, not the raw Identity token.</summary>
        [Required]
        public required string Token { get; set; }
    }
}
