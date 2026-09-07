namespace simpleShare_backend.Contracts.Invitation;

public class JoinGroupResponse
{
    public Guid GroupId { get; set; }
    public Guid MemberId { get; set; }

    /// <summary>
    /// True when the caller was already in the group. A shared link regularly
    /// reaches someone who is, and that is a success, not an error.
    /// </summary>
    public bool AlreadyMember { get; set; }
}
