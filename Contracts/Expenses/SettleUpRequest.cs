namespace simpleShare_backend.Contracts.Expenses;

public class SettleUpRequest
{
    public Guid? FromMemberId { get; set; }
    public Guid ToMemberId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
}
