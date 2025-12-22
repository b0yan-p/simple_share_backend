
using Ardalis.Specification;
using simpleShare_backend.Domain;

namespace simpleShare_backend.Services.Specifications;

public class GroupSearchList : Specification<Group>
{
    public GroupSearchList(string? query = "")
    {
        if (!string.IsNullOrEmpty(query))
        {
            _ = Query.Where(g => g.Name.Contains(query));
        }

        _ = Query.OrderByDescending(g => g.ModifiedAt)
            .ThenByDescending(g => g.CreatedAt);
    }
}
