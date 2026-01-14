namespace simpleShare_backend.Models.Application.Activity;

public sealed record ExpenseActivityData(
    string Description,
    decimal Amount,
    string[]? ChangedFields = null
);
