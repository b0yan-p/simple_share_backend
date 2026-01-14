using Microsoft.EntityFrameworkCore.Metadata.Internal;
using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Contracts.Activity;

public class ActivityResponse
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public ActivityType Type { get; set; }
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public Guid ActorMemberId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public ActivityEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string Data { get; set; } = string.Empty;
}
