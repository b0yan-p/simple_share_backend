
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
}
