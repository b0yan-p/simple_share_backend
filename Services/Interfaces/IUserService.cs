using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Services;

public interface IUserService
{
    Task<User?> GetUserAsync(Guid id);
}
