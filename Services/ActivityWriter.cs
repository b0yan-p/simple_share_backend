using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using simpleShare_backend.Data;
using simpleShare_backend.Models.Application.Activity;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Enums;
using simpleShare_backend.Services.Interfaces;

namespace simpleShare_backend.Services;

public class ActivityWriter(ApplicationDbContext db) : IActivityWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Enqueue<T>(ActivityEvent<T> evt)
    {
        var sourceEventId = evt.SourceEventId ?? Guid.NewGuid();
        var createdAt = evt.CreatedAt ?? DateTime.UtcNow;
        var activityId = Guid.NewGuid();

        db.Activities.Add(
            new Activity
            {
                Id = activityId,
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

        var outboxPayload = new ActivityCreateOutboxPayload(
            ActivityId: activityId,
            GroupId: evt.GroupId,
            ActorMemberId: evt.ActorMemberId,
            Type: evt.Type,
            EntityType: evt.EntityType,
            EntityId: evt.EntityId,
            CreatedAt: createdAt
        );

        db.OutboxMessages.Add(new OutboxMessage()
        {
            Id = Guid.NewGuid(),
            CreatedAt = createdAt,
            Type = evt.Type,
            PayloadJson = JsonSerializer.Serialize(outboxPayload, JsonOptions),
            AttemptCount = 0
        });
    }
}
