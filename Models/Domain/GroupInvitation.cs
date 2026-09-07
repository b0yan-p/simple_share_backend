using simpleShare_backend.Domain;

namespace simpleShare_backend.Models.Domain;

/// <summary>
/// A shareable link that lets anyone holding the token join one group.
/// A group can have many over time, but only one active at a time — enforced by
/// a filtered unique index in ApplicationDbContext.
/// </summary>
public class GroupInvitation : BaseModel<Guid>
{
    public Guid GroupId { get; set; }
    public Group Group { get; set; } = null!;

    public string Token { get; set; } = null!;

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? RevokedAtUtc { get; set; }
    public int UseCount { get; set; }
}
