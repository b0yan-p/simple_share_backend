using Ardalis.Specification;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Services.Specifications.Members;

public class MemberByEmailSpec : Specification<Member>
{
    public MemberByEmailSpec(string? email)
    {
        if (string.IsNullOrEmpty(email))
            return;

        _ = Query.Where(x => x.NormalizedEmail == email.Trim().ToUpper());
    }
}
