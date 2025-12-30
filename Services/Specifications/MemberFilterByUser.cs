using Ardalis.Specification;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Services.Specifications;

public class MemberFilterByUser : Specification<Member>
{
    public MemberFilterByUser(Guid userId)
    {
        _ = Query.Where(m => m.UserId == userId);
    }
}
