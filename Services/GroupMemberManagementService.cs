using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Data;
using simpleShare_backend.Infrastructure;
using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Services;

public class GroupMemberManagementService(
    IGroupService groupService,
    IMemberService memberService,
    IGroupMemberService groupMemberService,
    IRepositoryAsync repo,
    ApplicationDbContext dbContext
) : IGroupMemberManagementService
{

    public async Task<AddGroupMembersResponse> AddGroupMembersAsync(Guid groupId, AddGroupMembersRequest input)
    {
        _ = await groupService.GetGroupAsync(groupId)
            ?? throw new Exception("GroupId does not exist");

        AddGroupMembersResponse response = new();

        foreach (var item in input.Members)
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync();
            try
            {
                GroupMember? gm = null;
                if (item.MemberId is not null)
                    gm = await groupMemberService.AddExistingMemberIntoGroupAsync(item.MemberId.Value, groupId);
                else
                {
                    // Create new virtual member
                    var member = await memberService.GetOrCreateVirtualMemberAsync(item);
                    if (member is null)
                    {
                        await tx.RollbackAsync();
                        response.ErrorMessages.Add($"Member {item.DisplayName} failed to create");
                        continue;
                    }
                    else
                        gm = await groupMemberService.CreateGroupMemberDefaultAsync(member, groupId);

                    if (item.InviteUser)
                    {
                        // TODO send invite view email
                    }
                }

                if (gm is null)
                {
                    await tx.RollbackAsync();
                    response.ErrorMessages.Add($"Member does not exist");
                    continue;
                }
                else
                {
                    await repo.SaveChangesAsync();
                    await tx.CommitAsync();
                    response.Members.Add(gm.ToResponse());
                }
            }
            catch (Exception e)
            {
                await tx.RollbackAsync();
                dbContext.ChangeTracker.Clear();
                response.ErrorMessages.Add(e.Message);
            }
        }

        return response;
    }
}
