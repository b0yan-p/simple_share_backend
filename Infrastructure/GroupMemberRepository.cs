
using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Data;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Infrastructure;

public class GroupMemberRepository(ApplicationDbContext db) : IGroupMemberRepository
{
    public async Task<ICollection<GroupMember>> GetAllAsync(Guid groupId)
    {
        return await db.GroupMembers
            .Where(x =>
                x.GroupId == groupId
                && x.GroupMemberStatus != GroupMemberStatus.Deleted
                && x.LeftAt == null
            )
            .Include(x => x.Member)
            .ToListAsync();
    }

    public async Task<GroupMember?> GetGroupMemberDefaultAsync(Guid groupId, Guid memberId)
    {
        return await db.GroupMembers
            .Where(x => x.GroupId == groupId && x.MemberId == memberId)
            .Include(x => x.Member)
            .FirstOrDefaultAsync();
    }

    public async Task<GroupMember> CreateGroupMemberAsync(GroupMember entity)
    {
        _ = await db.GroupMembers.AddAsync(entity);
        return entity;
    }

    public GroupMember UpdateGroupMember(GroupMember item)
    {
        db.GroupMembers.Update(item);
        return item;
    }

    public async Task<int> SaveChangesAsync()
    {
        return await db.SaveChangesAsync();
    }

    public async Task<int> GetGroupMemberCountAsync(Guid groupId)
    {
        return await db.GroupMembers
            .Where(x => x.GroupId == groupId && x.LeftAt == null && x.GroupMemberStatus != GroupMemberStatus.Deleted)
            .CountAsync();
    }

    public async Task<bool> IsGroupMemberAsync(Guid groupId, Guid memberId)
    {
        return await db.GroupMembers
            .AsNoTracking()
            .Where(gm =>
                gm.GroupId == groupId &&
                gm.MemberId == memberId &&
                gm.LeftAt == null &&
                gm.GroupMemberStatus != GroupMemberStatus.Deleted
            ).AnyAsync();
    }

    public async Task<bool> IsUserGroupMember(Guid groupId, Guid userId)
    {
        return await db.GroupMembers
            .AsNoTracking()
            .Select(gm => new { gm.GroupId, gm.Member.UserId, gm.LeftAt, gm.GroupMemberStatus })
            .Where(gm =>
                gm.GroupId == groupId &&
                gm.UserId == userId &&
                gm.LeftAt == null &&
                gm.GroupMemberStatus != GroupMemberStatus.Deleted
            ).AnyAsync();
    }
}
