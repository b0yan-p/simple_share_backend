using System.ComponentModel.DataAnnotations;

namespace simpleShare_backend.Contracts.Group;

public class CreateGroupRequest
{
    [Required]
    [MaxLength(48)]
    public required string Name { get; set; } = string.Empty;
    public bool? SimplifyDebts { get; set; } = false;
}
