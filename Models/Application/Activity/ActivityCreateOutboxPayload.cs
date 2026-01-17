using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Models.Application.Activity;

public sealed record ActivityCreateOutboxPayload(
    Guid ActivityId,
    Guid GroupId,
    Guid ActorMemberId,
    ActivityType Type,
    ActivityEntityType EntityType,
    Guid EntityId,
    DateTime CreatedAt
);
