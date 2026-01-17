using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Models.Application.Activity;

public sealed record ActivityEvent<T>(
    ActivityType Type,
    ActivityEntityType EntityType,
    Guid EntityId,
    Guid GroupId,
    Guid ActorMemberId,
    T Data,
    DateTime? CreatedAt = null,
    Guid? SourceEventId = null
);
