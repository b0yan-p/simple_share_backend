namespace simpleShare_backend.Contracts.Group;

public class GroupResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool SimplifyDebts { get; set; } = false;
    public DateTime CreatedAt { get; set; } = default;
    public DateTime? DeletedAt { get; set; } = null;

    // > 0 means user is owed money, < 0 means user owes money,
    public Decimal NetBalance { get; set; } = 0;
}
