using simpleShare_backend.Models.Application;

namespace simpleShare_backend.Contracts.Group;

public class MySummaryResponse
{
   public decimal PaidTotal { get; set; }
   public decimal OwedTotal { get; set; }
   public decimal Net => PaidTotal - OwedTotal;
   public ICollection<MyDebtEdge> DebtEdges { get; set; } = [];
}
