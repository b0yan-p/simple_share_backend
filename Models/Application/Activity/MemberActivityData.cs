namespace simpleShare_backend.Models.Application.Activity;

public sealed record MemberActivityData(
    Guid TargetMemberId,
    string TargetMemberName

);