namespace simpleShare_backend.Contracts.Invitation;

/// <summary>
/// Served without authentication, so it carries only what the invite screen has
/// to show — never member names or balances.
/// </summary>
public class InvitePreviewResponse
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
}
