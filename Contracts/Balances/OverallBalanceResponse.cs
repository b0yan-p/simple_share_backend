using simpleShare_backend.Models.Application;
namespace simpleShare_backend.Contracts.Balances;

public class OverallBalanceResponse
{
    public int GroupCount { get; set; }
    public IReadOnlyList<CurrencyBalance> Balances { get; set; } = [];
}
