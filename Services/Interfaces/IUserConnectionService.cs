using simpleShare_backend.Contracts.Member;

namespace simpleShare_backend.Services;

public interface IUserConnectionService
{
   Task<bool> AddUserConnectionBulk(IReadOnlyCollection<Guid> userIds);
   Task<List<MemberListResponse>> GetMyConnectionsAsync(Guid userId);
}
