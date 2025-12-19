namespace simpleShare_backend.Dtos
{
    public class NewUserDto
    {
        public required Guid Id { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public string FullName => $"{FirstName} {LastName}";
        public required string Token { get; set; }
        public required string Email { get; set; }
    }
}