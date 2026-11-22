using simpleShare_backend.Models.Enums;
using System.ComponentModel.DataAnnotations;
namespace simpleShare_backend.Contracts.Group;

public class UpdateGroupRequest
{
    [EnumDataType(typeof(GroupType))]
    public GroupType? Type { get; set; }
    public string? CoverImageKey { get; set; }
    public required Guid Id { get; set; }
    [MaxLength(48)] public string? Name { get; set; }
    public bool? SimplifyDebts { get; set; }
    public string? Currency { get; set; }
}
