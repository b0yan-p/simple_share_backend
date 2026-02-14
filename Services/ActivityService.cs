using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Common;
using simpleShare_backend.Common.Exceptions;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
            ?? throw new NotFoundAppException("member_not_found", "Member not found!");

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

        var rows = await baseQuery
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip(skip)
            .Take(take)
            .Select(a => new
            {
                a.Id,
                a.CreatedAt,
                a.ActorMemberId,
                ActorName = a.ActorMember.DisplayName,
                a.GroupId,
                GroupName = a.Group.Name,
                a.DataJson,
                a.EntityId,
                a.EntityType,
                a.Type
            })
            .ToListAsync(ct);

        var settleUpMemberIds = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (row.EntityType != ActivityEntityType.SettleUp)
                continue;

            if (TryReadSettleUpData(row.DataJson, out var settleUpData))
            {
                if (settleUpData.FromMemberId != Guid.Empty)
                    settleUpMemberIds.Add(settleUpData.FromMemberId);

                if (settleUpData.ToMemberId != Guid.Empty)
                    settleUpMemberIds.Add(settleUpData.ToMemberId);
            }
        }

        var memberNames = settleUpMemberIds.Count == 0
            ? []
            : await db.Members
                .AsNoTracking()
                .Where(m => settleUpMemberIds.Contains(m.Id))
                .Select(m => new { m.Id, m.DisplayName })
                .ToDictionaryAsync(m => m.Id, m => m.DisplayName, ct);

        var activities = rows.Select(a => new ActivityResponse
        {
            Id = a.Id,
            CreatedAt = a.CreatedAt,
            ActorMemberId = a.ActorMemberId,
            GroupId = a.GroupId,
            ActorName = a.ActorName,

            GroupName = a.GroupName,
            Data = BuildActivityData(a.EntityType, a.DataJson, memberNames),
            EntityId = a.EntityId,
            EntityType = a.EntityType,
            Type = a.Type
        }).ToList();

        return new PageResponse<ActivityResponse>(activities, totalCount, skip, take);
    }

    private static JsonElement BuildActivityData(
        ActivityEntityType entityType,
        string dataJson,
        IReadOnlyDictionary<Guid, string> memberNames
    )
    {
        if (entityType != ActivityEntityType.SettleUp)
            return JsonSerializer.Deserialize<JsonElement>(dataJson);

        if (!TryReadSettleUpData(dataJson, out var settleUpData))
            return JsonSerializer.Deserialize<JsonElement>(dataJson);

        var node = JsonNode.Parse(dataJson) as JsonObject ?? new JsonObject();

        if (memberNames.TryGetValue(settleUpData.FromMemberId, out var fromName))
            node["fromMemberName"] = fromName;

        if (memberNames.TryGetValue(settleUpData.ToMemberId, out var toName))
            node["toMemberName"] = toName;

        return JsonSerializer.SerializeToElement(node);
    }

    private static bool TryReadSettleUpData(string json, out SettleUpActivityData data)
    {
        try
        {
            data = JsonSerializer.Deserialize<SettleUpActivityData>(json, JsonOptions);
            return data is not null;
        }
        catch (JsonException)
        {
            data = default!;
            return false;
        }
    }
}
