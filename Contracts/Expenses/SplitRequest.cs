namespace simpleShare_backend.Contracts.Expenses;

public class SplitRequest
{
    public required Guid MemberId { get; set; }
    public required decimal Amount { get; set; }
}
