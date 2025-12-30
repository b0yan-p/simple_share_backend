using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Infrastructure;

public interface IUserRepository
{
    Task<User?> GetUserAsync(Guid id);
}
