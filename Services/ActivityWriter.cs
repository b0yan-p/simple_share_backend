using System.Text.Json;
using simpleShare_backend.Data;
using simpleShare_backend.Models.Application.Activity;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Services.Interfaces;

namespace simpleShare_backend.Services;

public class ActivityWriter(ApplicationDbContext db) : IActivityWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Enqueue<T>(ActivityEvent<T> evt)
    {
        var sourceEventId = evt.SourceEventId ?? Guid.NewGuid();
        var createdAt = evt.CreatedAt ?? DateTime.UtcNow;

        db.Activities.Add(
            new Activity
            {
                Id = Guid.NewGuid(),
                SourceEventId = sourceEventId,
                CreatedAt = createdAt,
                GroupId = evt.GroupId,
                Type = evt.Type,
                ActorMemberId = evt.ActorMemberId,
                EntityId = evt.EntityId,
                EntityType = evt.EntityType,
                DataJson = JsonSerializer.Serialize(evt.Data, JsonOptions)
            }
        );

        // TODO implement OutboxMessage later
    }
}
