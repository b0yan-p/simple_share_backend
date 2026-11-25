namespace simpleShare_backend.Contracts.Auth
{
    public class LoginUserResponse
    {
        public bool HasPassword { get; set; }
        public required Guid Id { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public string FullName => $"{FirstName} {LastName}";
        public required string DisplayName { get; set; }
        public required string Token { get; set; }
        public required string Email { get; set; }
    }
}