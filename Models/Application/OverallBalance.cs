namespace simpleShare_backend.Models.Application;
public class CurrencyBalance
{
    public string Currency { get; set; } = "";
    public decimal OwedToMeTotal { get; set; }
    public decimal IOweTotal { get; set; }
    public decimal Net => OwedToMeTotal - IOweTotal;
    public int GroupCount { get; set; }
}
public class OverallBalance
{
    public int GroupCount { get; set; }
    public IReadOnlyList<CurrencyBalance> Balances { get; set; } = [];
}
