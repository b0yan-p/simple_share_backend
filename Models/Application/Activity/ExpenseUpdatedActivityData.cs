namespace simpleShare_backend.Models.Application.Activity;

public sealed record ExpenseUpdatedActivityData
(
    string DescriptionBefore,
    string DescriptionAfter,
    decimal AmountBefore,
    decimal AmountAfter,
    string[] ChangedFields
);