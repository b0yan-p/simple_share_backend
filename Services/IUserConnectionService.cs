namespace simpleShare_backend.Services;

public interface IUserConnectionService
{
   Task<bool> AddUserConnectionBulk(IReadOnlyCollection<Guid> userIds);
}
