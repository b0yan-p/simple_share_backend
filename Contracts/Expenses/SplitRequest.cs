using System.Text.Json.Serialization;
namespace simpleShare_backend.Contracts.Expenses;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class SplitRequest
{
    public required Guid MemberId { get; set; }
    public required decimal OriginalAmount { get; set; }
}
