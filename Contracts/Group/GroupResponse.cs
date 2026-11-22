using simpleShare_backend.Models.Enums;
namespace simpleShare_backend.Contracts.Group;

public class GroupResponse
{
    public GroupType Type { get; set; }
    public string? CoverImageKey { get; set; }
    public bool CurrencyLocked { get; set; }
    public bool CanChangeCurrency { get; set; }
    public string Currency { get; set; } = "";
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool SimplifyDebts { get; set; } = false;
    public DateTime CreatedAt { get; set; } = default;
    public DateTime? DeletedAt { get; set; } = null;

    // > 0 means user is owed money, < 0 means user owes money,
    public Decimal NetBalance { get; set; } = 0;
}
