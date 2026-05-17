using simpleShare_backend.Contracts.Group;
using simpleShare_backend.Domain;

namespace simpleShare_backend.Common.Extensions;

public static class GroupResponseExtension
{
    public static GroupResponse ToResponse(this Group group)
    {
        return new GroupResponse
        {
            Id = group.Id,
            Name = group.Name,
            SimplifyDebts = group.SimplifyDebts,
            CreatedAt = group.CreatedAt,
            DeletedAt = group.DeletedAt,
        };
    }
}
