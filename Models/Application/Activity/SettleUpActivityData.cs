namespace simpleShare_backend.Models.Application.Activity;

public sealed record SettleUpActivityData
(
    Guid FromMemberId,
    Guid ToMemberId,
    decimal Amount
);