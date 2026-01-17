using simpleShare_backend.Models.Base;
using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Models.Domain;

public class OutboxMessage : BaseModel<Guid>, ICreatable
{
    // General data
    public ActivityType Type { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }

    // Processing state
    public DateTime? ProcessedAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LockedAt { get; set; } // Time when some worker has locked OutboxMessage for processing
    public Guid? LockId { get; set; }       // Identification id of worker that processing OutboxMessage

    // Diagnostic
    public string? LastError { get; set; }
}
