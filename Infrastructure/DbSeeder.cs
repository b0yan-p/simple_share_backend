using Microsoft.AspNetCore.Identity;
using simpleShare_backend.Interfaces;
using simpleShare_backend.Models.Identity;
using simpleShare_backend.Services;

namespace simpleShare_backend.Infrastructure;

public static class DbSeeder
{
    // Seed users added at startup if they don't already exist.
    // Change these values as needed.
    private static readonly (string FirstName, string LastName, string Email, string Password)[] SeedUsers =
    [
        ("Pero", "Peric", "pero@peric.com", "Mojal1"),
        ("Chox", "Chokic", "chox@chokic.com", "Mojal1"),
    ];

    public static async Task SeedUsersAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var userManager = sp.GetRequiredService<UserManager<User>>();
        var memberService = sp.GetRequiredService<IMemberService>();
        var connectionService = sp.GetRequiredService<IUserConnectionService>();
        var repo = sp.GetRequiredService<IRepositoryAsync>();

        var seededUserIds = new List<Guid>();

        foreach (var seed in SeedUsers)
        {
            // Idempotent: reuse the existing user if one already has this email.
            var user = await userManager.FindByEmailAsync(seed.Email);
            if (user is not null)
            {
                seededUserIds.Add(user.Id);
                continue;
            }

            user = new User
            {
                FirstName = seed.FirstName,
                LastName = seed.LastName,
                Email = seed.Email,
                UserName = seed.Email
            };

            var createUser = await userManager.CreateAsync(user, seed.Password);
            if (!createUser.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to seed user '{seed.Email}': " +
                    string.Join("; ", createUser.Errors.Select(e => e.Description)));

            var roleResult = await userManager.AddToRoleAsync(user, "User");
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to add role for seed user '{seed.Email}': " +
                    string.Join("; ", roleResult.Errors.Select(e => e.Description)));

            var existingMember = await memberService.GetMemberByUserIdAsync(user.Id);
            if (existingMember is null)
            {
                await memberService.CreateMemberFromUserAsync(user);
                await repo.SaveChangesAsync();
            }

            seededUserIds.Add(user.Id);
        }

        // Connect the seeded users to each other. AddUserConnectionBulk handles
        // pair ordering (UserAId < UserBId) and skips pairs that already exist.
        if (seededUserIds.Count >= 2)
        {
            var added = await connectionService.AddUserConnectionBulk(seededUserIds);
            if (added)
                await repo.SaveChangesAsync();
        }
    }
}
