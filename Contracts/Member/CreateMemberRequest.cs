namespace simpleShare_backend.Contracts.Member;

public class CreateMemberRequest
{
    public required string DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
}
