namespace simpleShare_backend.Contracts.Member;

public class AddGroupMembersRequest
{
    public List<AddGroupMemberItemRequest> Members { get; set; } = [];
}

public class AddGroupMemberItemRequest
{
    public Guid? MemberId { get; set; } // Add existing friend/member

    public string? DisplayName { get; set; } // Create virtual member
    public bool InviteUser { get; set; } = false;
    public string? Email { get; set; } // required if InviteUser = true
}


