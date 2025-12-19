using System.ComponentModel.DataAnnotations;

namespace simpleShare_backend.Dtos
{
    public class LoginUserDto
    {
        [Required]
        [EmailAddress]
        public required string Email { get; set; }

        [Required]
        public required string Password { get; set; }
    }
}