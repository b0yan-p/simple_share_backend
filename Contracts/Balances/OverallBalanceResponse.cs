namespace simpleShare_backend.Contracts.Balances;

public class OverallBalanceResponse
{
    public decimal OwedToMeTotal { get; set; }
    public decimal IOweTotal { get; set; }
    public decimal Net => OwedToMeTotal - IOweTotal;
    public int GroupCount { get; set; }
}
