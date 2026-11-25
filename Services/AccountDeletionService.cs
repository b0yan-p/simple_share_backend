using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Data;
using simpleShare_backend.Models.Enums;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Services;

public class AccountDeletionService(ApplicationDbContext db, UserManager<User> users)
{
    public async Task DeleteAsync(Guid userId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (user is null) return;

        var member = await db.Members.SingleOrDefaultAsync(m => m.UserId == userId, ct);
        if (member is not null)
        {
            // Lock every group this member could own, including a concurrent promotion.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT g."Id" FROM "Groups" g
                JOIN "GroupMembers" gm ON gm."GroupId" = g."Id"
                WHERE gm."MemberId" = {member.Id}
                ORDER BY g."Id" FOR UPDATE OF g
                """, ct);
            var ownerships = await db.GroupMembers.Where(gm => gm.MemberId == member.Id &&
                gm.GroupRole == GroupRole.Owner).ToListAsync(ct);
            foreach (var owner in ownerships)
            {
                owner.GroupRole = GroupRole.Member;
                if (owner.LeftAt != null || owner.GroupMemberStatus != GroupMemberStatus.Active)
                    continue;
                var successor = await db.GroupMembers.Where(gm => gm.GroupId == owner.GroupId &&
                    gm.MemberId != member.Id && gm.LeftAt == null &&
                    gm.GroupMemberStatus == GroupMemberStatus.Active &&
                    gm.Member.DeletedAt == null && gm.Member.UserId != null)
                    .OrderByDescending(gm => gm.GroupRole == GroupRole.Owner)
                    .ThenBy(gm => gm.JoinedAt).ThenBy(gm => gm.MemberId)
                    .FirstOrDefaultAsync(ct);
                if (successor is not null) successor.GroupRole = GroupRole.Owner;
            }
            member.UserId = null;
            member.Email = member.NormalizedEmail = member.FirstName = member.LastName = null;
            member.ModifiedAt = DateTime.UtcNow;
        }
        await db.UserConnections
            .Where(c => c.UserAId == userId || c.UserBId == userId)
            .ExecuteDeleteAsync(ct);

        await db.SaveChangesAsync(ct);
        (await users.DeleteAsync(user)).EnsureSucceeded("Delete account");
        await transaction.CommitAsync(ct);
    }
}
