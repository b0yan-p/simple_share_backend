using simpleShare_backend.Models.Application;

namespace simpleShare_backend.Contracts.Balances;

public class BalanceResponse
{
    public Guid GroupId { get; set; }
    public IReadOnlyList<MemberBalance> Members { get; set; } = [];
}
