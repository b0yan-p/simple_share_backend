namespace simpleShare_backend.Contracts.Member;

public class AddGroupMembersResponse
{
    public List<GroupMemberResponse> Members { get; set; } = [];
    public List<string> ErrorMessages { get; set; } = [];
}
