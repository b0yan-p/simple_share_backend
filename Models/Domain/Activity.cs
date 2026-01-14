using simpleShare_backend.Domain;
using simpleShare_backend.Models.Base;
using simpleShare_backend.Models.Enums;

namespace simpleShare_backend.Models.Domain;

public class Activity : BaseModel<Guid>, ICreatable
{
    public DateTime CreatedAt { get; set; }

    public Guid GroupId { get; set; }
    public Guid ActorMemberId { get; set; }

    public ActivityType Type { get; set; }
    public ActivityEntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    // Snapshot/payload
    public string DataJson { get; set; } = "{}";
    public Guid SourceEventId { get; set; }

    public Group Group { get; set; } = default!;
    public Member ActorMember { get; set; } = default!;
}

/*
    1. Expense
        - Add       - John added "Water" in "Group 1"
                        (You owe $7.34)
                        (Yesterday, 23:30)

        - Update    -  John updated (description/amount/involved user)
                        (You owe BAM 10.22)
                        (Yesterday, 23:30)

        - Delete    - You deleted "Water" in "Group 1"
                        (You get back $4)
                        (Yesterday, 23:30)

    2. Settle
        - USER_A paid to USER_B - You paid John in "Group 1"
                                    (You paid $0.18)
                                    (2 days ago, 23:30)

    3. Members
        - Added new member      - You added John in "Group 1"
                                    (5 Jan, 23:30)

        - Removed group member  - John removed Michael from "Group 1"
                                    (10/12/2025, 23:30)

 
    # Type of messages

        1. {who} {action} {what} in {where} - You added "Ice cream" in "Group 1"  -> actions (add, delete, update)
        2. {who} {action} {whom} in {where} - John paid you in "Group 1" -> actions (paid)
        3. {who} {action} {what_member} to the group "Group 1" -> actions (add, remove)

*/
