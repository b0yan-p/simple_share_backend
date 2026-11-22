using simpleShare_backend.Models.Enums;
namespace simpleShare_backend.Contracts.Group;

public class GroupOverviewResponse
{
    public GroupType Type { get; set; }
    public string? CoverImageKey { get; set; }
    public string Currency { get; set; } = "";
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MySummaryResponse MyDebtSummary { get; set; } = default!;
    public int MemberCount { get; set; }
    public Guid CurrentMemberId { get; set; }
}
