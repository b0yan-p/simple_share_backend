namespace simpleShare_backend.Contracts.Invitation;

public class GroupInvitationResponse
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; }
    public int UseCount { get; set; }
}
