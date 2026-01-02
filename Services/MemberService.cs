using simpleShare_backend.Infrastructure;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Identity;
using simpleShare_backend.Services.Specifications;

namespace simpleShare_backend.Services;

public class MemberService(IRepositoryAsync repo) : IMemberService
{
    public async Task<Member> CreateMemberFromUserAsync(User user)
    {
        var newMember = new Member
        {
            Id = Guid.NewGuid(),
            Email = user.Email,
            NormalizedEmail = user.Email?.ToLower(),
            UserId = user.Id,
            DisplayName = $"{user.FirstName} {user.LastName}",
            FirstName = user.FirstName,
            LastName = user.LastName,
            CreatedAt = DateTime.UtcNow,
        };

        return await repo.CreateAsync<Member, Guid>(newMember);
    }

    public async Task<Member?> GetMemberAsync(Guid id)
    {
        return await repo.GetByIdDefaultAsync<Member, Guid>(id);
    }

    public async Task<Member?> GetMemberByUserIdAsync(Guid userId)
    {
        MemberFilterByUser spec = new(userId);
        var member = await repo.GetSingleDefaultAsync<Member, Guid>(spec, true);
        return member;
    }
}
