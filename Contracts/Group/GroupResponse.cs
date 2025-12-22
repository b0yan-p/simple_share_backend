namespace simpleShare_backend.Contracts.Group;

public class GroupResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool SimplifyDebts { get; set; } = false;
    public DateTime CreatedAt { get; set; } = default;
}
