
using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Data;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Infrastructure;

public class UserRepository(ApplicationDbContext dbContext) : IUserRepository
{
    public async Task<User?> GetUserAsync(Guid id)
    {
        return await dbContext.Users.Where(x => x.Id == id).FirstOrDefaultAsync();
    }
}
