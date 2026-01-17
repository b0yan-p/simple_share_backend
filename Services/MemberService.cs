using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Infrastructure;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Identity;
using simpleShare_backend.Services.Specifications;
using simpleShare_backend.Services.Specifications.Members;

namespace simpleShare_backend.Services;

public class MemberService(IRepositoryAsync repo) : IMemberService
{
    public async Task<Member> CreateMemberFromUserAsync(User user)
    {
        var newMember = new Member
        {
            Id = Guid.NewGuid(),
            Email = user.Email,
            NormalizedEmail = user.Email?.Trim().ToUpper(),
            UserId = user.Id,
            DisplayName = $"{user.FirstName} {user.LastName}",
            FirstName = user.FirstName,
            LastName = user.LastName,
            CreatedAt = DateTime.UtcNow,
        };

        return await repo.CreateAsync<Member, Guid>(newMember);
    }

    public async Task<Member?> GetOrCreateVirtualMemberAsync(AddGroupMemberItemRequest input)
    {
        if (string.IsNullOrEmpty(input.DisplayName))
            throw new Exception("Display name cannot be empty!");

        if (input.InviteUser && string.IsNullOrEmpty(input.Email))
            throw new Exception("Email is required");

        if (input.InviteUser)
        {
            var spec = new MemberByEmailSpec(input.Email);
            var dbMember = await repo.GetSingleDefaultAsync<Member, Guid>(spec);

            if (dbMember is not null)
                return dbMember;
        }

        Member member = new()
        {
            Id = Guid.NewGuid(),
            DisplayName = input.DisplayName.Trim(),
            Email = input.Email?.Trim(),
            NormalizedEmail = input.Email?.Trim().ToUpper(),
            CreatedAt = DateTime.UtcNow,
        };

        return await repo.CreateAsync<Member, Guid>(member);
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
