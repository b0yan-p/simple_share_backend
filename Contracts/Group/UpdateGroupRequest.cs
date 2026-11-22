using System.ComponentModel.DataAnnotations;
namespace simpleShare_backend.Contracts.Group;

public class UpdateGroupRequest
{
    public required Guid Id { get; set; }
    [MaxLength(48)] public string? Name { get; set; }
    public bool? SimplifyDebts { get; set; }
    public string? Currency { get; set; }
}
