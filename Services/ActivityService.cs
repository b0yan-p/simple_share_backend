using System.Threading.Tasks.Dataflow;
using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Common;
using simpleShare_backend.Contracts.Activity;
using simpleShare_backend.Data;
using simpleShare_backend.Models.Application.Activity;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Enums;
using simpleShare_backend.Services.Interfaces;

namespace simpleShare_backend.Services;

public class ActivityService(
    ApplicationDbContext db,
    IMemberService memberService
) : IActivityService
{
    public ActivityEvent<ExpenseUpdatedActivityData> AddExpenseUpdatedActivity(
        Expense oldExpense,
        Expense newExpense,
        Guid ActorMemberId
    )
    {
        var descBefore = oldExpense.Description;
        var descAfter = newExpense.Description;

        var amountBefore = oldExpense.TotalAmount;
        var amountAfter = newExpense.TotalAmount;

        var changedFields = new List<string>(2);

        if (amountBefore != amountAfter)
            changedFields.Add("amount");

        if (string.CompareOrdinal(descBefore, descAfter) != 0)
            changedFields.Add("description");


        var data = new ExpenseUpdatedActivityData(
            DescriptionBefore: descBefore,
            DescriptionAfter: descAfter,
            AmountAfter: amountAfter,
            AmountBefore: amountBefore,
            ChangedFields: [.. changedFields]
        );

        return new ActivityEvent<ExpenseUpdatedActivityData>
        (
            ActorMemberId: ActorMemberId,
            CreatedAt: DateTime.UtcNow,
            Data: data,
            EntityId: newExpense.Id,
            EntityType: ActivityEntityType.Expense,
            Type: ActivityType.ExpenseUpdated,
            GroupId: newExpense.GroupId
        );
    }

    public async Task<PageResponse<ActivityResponse>> GetMyActivitiesAsync(
         Guid userId,
         int skip,
         int take,
         CancellationToken ct = default
     )
    {
        var member = await memberService.GetMemberByUserIdAsync(userId)
            ?? throw new Exception("Member not found!");

        // Fetch all groupIds where i am member at
        var groupIds = await db.GroupMembers
            .Where(x =>
                x.MemberId == member.Id &&
                x.LeftAt == null &&
                x.GroupMemberStatus != GroupMemberStatus.Deleted &&
                x.Group.DeletedAt == null
            )
            .Select(x => x.GroupId)
            .ToListAsync(ct);

        if (groupIds.Count == 0)
            return new PageResponse<ActivityResponse>([], 0, skip, take);

        var baseQuery = db.Activities
            .AsNoTracking()
            .Where(a => groupIds.Contains(a.GroupId));

        var totalCount = await baseQuery.CountAsync(ct);

        var activities = await baseQuery
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip(skip)
            .Take(take)
            .Select(a => new ActivityResponse
            {
                Id = a.Id,
                CreatedAt = a.CreatedAt,
                ActorMemberId = a.ActorMemberId,
                ActorName = a.ActorMember.DisplayName,
                GroupId = a.GroupId,
                GroupName = a.Group.Name,
                Data = a.DataJson,
                EntityId = a.EntityId,
                EntityType = a.EntityType,
                Type = a.Type
            })
            .ToListAsync(ct);

        return new PageResponse<ActivityResponse>(activities, totalCount, skip, take);
    }
}
