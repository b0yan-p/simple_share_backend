
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
    public DbSet<GroupMember> GroupMembers { get; set; }
    public DbSet<Expense> Expenses { get; set; }
    public DbSet<ExpensePayment> ExpensePayments { get; set; }
    public DbSet<ExpenseSplit> ExpenseSplits { get; set; }
    public DbSet<UserConnection> UserConnections { get; set; }
    public DbSet<Activity> Activities { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

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


        #region Index

        builder.Entity<Member>()
            .HasIndex(m => m.UserId)
            .IsUnique()
            .HasFilter("\"UserId\" IS NOT NULL");

        builder.Entity<Member>()
            .HasIndex(m => m.NormalizedEmail)
            .IsUnique()
            .HasFilter("\"NormalizedEmail\" IS NOT NULL");

        builder.Entity<GroupMember>()
            .HasIndex(x => new { x.GroupId, x.MemberId })
            .HasFilter("\"LeftAt\" IS NULL AND \"GroupMemberStatus\" <> 0");

        builder.Entity<Expense>()
            .HasIndex(e => new { e.GroupId, e.ExpenseDate });
        builder.Entity<Expense>()
            .HasIndex(e => new { e.GroupId, e.CreatedAt });
        builder.Entity<Expense>()
            .HasIndex(e => e.GroupId)
            .HasFilter("\"DeletedAt\" IS NULL");

        builder.Entity<ExpensePayment>()
            .HasIndex(e => new { e.PaidByMemberId, e.ExpenseId });
        builder.Entity<ExpensePayment>()
            .HasIndex(e => e.PaidByMemberId);

        builder.Entity<ExpenseSplit>()
            .HasIndex(e => e.OwedByMemberId);
        #endregion

        #region GroupMembers

        builder.Entity<GroupMember>()
            .HasKey(x => new { x.GroupId, x.MemberId });

        builder.Entity<GroupMember>()
            .HasOne(x => x.Group)
            .WithMany(x => x.GroupMembers)
            .HasForeignKey(x => x.GroupId);

        builder.Entity<GroupMember>()
            .HasOne(x => x.Member)
            .WithMany(x => x.GroupMembers)
            .HasForeignKey(x => x.MemberId);
        // #endregion Group members END

        #endregion

        #region Expenses

        builder.Entity<Expense>()
            .HasOne(e => e.Group)
            .WithMany(g => g.Expenses)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Expense>()
            .HasOne(e => e.CreatedByMember)
            .WithMany(m => m.CreatedExpenses)
            .HasForeignKey(e => e.CreatedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Expense>()
            .Property(x => x.TotalAmount)
            .HasPrecision(19, 4);

        #endregion

        #region ExpensePayment

        builder.Entity<ExpensePayment>()
            .HasKey(x => new { x.ExpenseId, x.PaidByMemberId });

        builder.Entity<ExpensePayment>()
            .Property(x => x.Amount)
            .HasPrecision(19, 4);

        builder.Entity<ExpensePayment>()
            .HasOne(x => x.Expense)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.ExpenseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ExpensePayment>()
            .HasOne(x => x.PaidByMember)
            .WithMany(x => x.ExpensePayments)
            .HasForeignKey(x => x.PaidByMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        #endregion

        #region ExpenseSplit

        builder.Entity<ExpenseSplit>()
            .HasKey(x => new { x.ExpenseId, x.OwedByMemberId });

        builder.Entity<ExpenseSplit>()
            .Property(x => x.Amount)
            .HasPrecision(19, 4);

        builder.Entity<ExpenseSplit>()
            .HasOne(x => x.Expense)
            .WithMany(x => x.Splits)
            .HasForeignKey(x => x.ExpenseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ExpenseSplit>()
            .HasOne(x => x.OwedByMember)
            .WithMany(x => x.ExpenseSplits)
            .HasForeignKey(x => x.OwedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        #endregion

        #region UserConnections

        builder.Entity<UserConnection>()
            .HasKey(x => new { x.UserAId, x.UserBId });

        builder.Entity<UserConnection>()
            .HasOne(e => e.UserA)
            .WithMany(u => u.ConnectionAsA)
            .HasForeignKey(x => x.UserAId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<UserConnection>()
            .HasOne(x => x.UserB)
            .WithMany(u => u.ConnectionsAsB)
            .HasForeignKey(x => x.UserBId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<UserConnection>()
            .ToTable(t =>
            {
                t.HasCheckConstraint("CK_UserConnection_NotSelf", "\"UserAId\" <> \"UserBId\"");
                t.HasCheckConstraint("CK_UserConnection_Ordered", "\"UserAId\" < \"UserBId\"");
            });

        #endregion

        #region Activity

        builder.Entity<Activity>(b =>
        {
            b.HasKey(x => x.Id);

            b.Property(x => x.DataJson)
                .IsRequired()
                .HasColumnType("text");

            b.HasOne(x => x.Group)
                .WithMany(g => g.Activities)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.ActorMember)
                .WithMany(m => m.ActivitiesAsActor)
                .HasForeignKey(x => x.ActorMemberId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(x => new { x.GroupId, x.CreatedAt, x.Id });
            b.HasIndex(x => new { x.EntityType, x.EntityId });
            b.HasIndex(x => x.SourceEventId).IsUnique();
        });

        #endregion

        #region OutboxMessage
        builder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(x => x.Id);

            b.Property(x => x.Type).IsRequired();
            b.Property(x => x.LastError).HasMaxLength(2000);
            b.Property(x => x.PayloadJson)
                .IsRequired()
                .HasColumnType("text");

            b.HasIndex(x => new { x.ProcessedAt, x.CreatedAt, x.Id });
        });
        #endregion

        // Seed Roles
        List<IdentityRole<Guid>> roles =
        [
            new() {
                    Name = "User",
                    NormalizedName = "USER",
                    Id = Guid.Parse("69d32c02-ba22-4ac6-bacf-2daa921f4993"),
                    // Static stamp so the seeded model is deterministic (avoids
                    // PendingModelChangesWarning from an auto-generated stamp).
                    ConcurrencyStamp = "b1c9e6d4-0f2a-4c3e-8a7b-1d5e9f0a2b3c"
                }
        ];

        builder.Entity<IdentityRole<Guid>>().HasData(roles);
    }
}
