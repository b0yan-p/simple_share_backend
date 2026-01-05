namespace simpleShare_backend.Models.Application;

public class GroupBalances
{
    public Guid GroupId { get; init; }
    public IReadOnlyList<MemberBalance> Members { get; init; } = [];
}
