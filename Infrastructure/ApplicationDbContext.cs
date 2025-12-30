
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Domain;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Data;

public class ApplicationDbContext(DbContextOptions dbContextOptions)
    : IdentityDbContext<User, IdentityRole<Guid>, Guid>(dbContextOptions)
{
    public DbSet<Group> Groups { get; set; }
    public DbSet<Member> Members { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Rename Identity tables to remove AspNet prefix
        builder.Entity<User>().ToTable("Users");

        builder.Entity<IdentityRole<Guid>>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");

        #region Indexes
        builder.Entity<Member>()
            .HasIndex(m => m.UserId)
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL");
        #endregion

        // Seed Roles
        List<IdentityRole<Guid>> roles =
        [
            new() {
                    Name = "User",
                    NormalizedName = "USER",
                    Id = Guid.Parse("69d32c02-ba22-4ac6-bacf-2daa921f4993")
                }
        ];

        builder.Entity<IdentityRole<Guid>>().HasData(roles);
    }
}
