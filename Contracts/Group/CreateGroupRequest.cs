using simpleShare_backend.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace simpleShare_backend.Contracts.Group;

public class CreateGroupRequest
{
    [EnumDataType(typeof(GroupType))]
    public GroupType Type { get; set; }
    [Required]
    [MaxLength(48)]
    public required string Name { get; set; } = string.Empty;
    [Required] public required string Currency { get; set; }
    public Guid? Id { get; set; }
    public bool? SimplifyDebts { get; set; }
}
