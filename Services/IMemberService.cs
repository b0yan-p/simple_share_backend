using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Services;

public interface IMemberService
{
    Task<Member?> GetMemberAsync(Guid id);
    // Task<Member> CreateMemberAsync(CreateMemberRequest input);
    Task<Member> CreateMemberFromUserAsync(User user);
    Task<Member?> GetMemberByUserIdAsync(Guid userId);
}
