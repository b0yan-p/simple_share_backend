using simpleShare_backend.Infrastructure;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Services;

public class UserService(IUserRepository repo) : IUserService
{
    public async Task<User?> GetUserAsync(Guid id)
    {
        return await repo.GetUserAsync(id);
    }
}
