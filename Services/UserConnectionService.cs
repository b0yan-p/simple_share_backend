using simpleShare_backend.Data;
using simpleShare_backend.Models.Domain;
using System.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;

namespace simpleShare_backend.Services;

public class UserConnectionService(ApplicationDbContext db) : IUserConnectionService
{
    public async Task<bool> AddUserConnectionBulk(IReadOnlyCollection<Guid> userIds)
    {
        var ids = userIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        ids.Sort((x, y) => new SqlGuid(x).CompareTo(new SqlGuid(y)));
        var idSet = ids.ToHashSet();

        if (idSet.Count < 2)
            return false;

        var existingPairs = await db.UserConnections
            .AsNoTracking()
            .Where(uc => idSet.Contains(uc.UserAId) && idSet.Contains(uc.UserBId))
            .Select(uc => new { uc.UserAId, uc.UserBId })
            .ToListAsync();

        var existing = existingPairs
            .Select(x => (x.UserAId, x.UserBId))
            .ToHashSet();

        var now = DateTime.UtcNow;

        List<UserConnection> toInsert = [];
        for (int i = 0; i < ids.Count; i++)
        {
            for (int j = i + 1; j < ids.Count; j++)
            {
                var a = ids[i];
                var b = ids[j];

                if (existing.Contains((a, b)))
                    continue;

                toInsert.Add(new()
                {
                    UserAId = a,
                    UserBId = b,
                    CreatedAt = now
                });
            }
        }

        if (toInsert.Count == 0)
            return false;

        db.UserConnections.AddRange(toInsert);

        return true;
    }
}
