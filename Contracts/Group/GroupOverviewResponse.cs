namespace simpleShare_backend.Contracts.Group;

public class GroupOverviewResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MySummaryResponse MyDebtSummary { get; set; } = default!;
    public int MemberCount { get; set; }
}
