using Ardalis.Specification;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Services.Specifications;

public class MemberByIds : Specification<Member>
{
    public MemberByIds(List<Guid> memberIds)
    {
        if (memberIds.Count == 0)
            return;

        _ = Query.AsNoTracking().Where(m => memberIds.Contains(m.Id));
    }
}
