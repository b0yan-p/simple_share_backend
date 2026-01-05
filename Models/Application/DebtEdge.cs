namespace simpleShare_backend.Models.Application;

public class DebtEdge
{
    public Guid DebtorMemberId { get; set; }
    public string DebtorDisplayName { get; set; } = "";
    public Guid CreditorMemberId { get; set; }
    public string CreditorDisplayName { get; set; } = "";

    public decimal Amount { get; set; }
}
