namespace simpleShare_backend.Common;

public class PageResponse<T>(List<T> data, int totalCount, int skip, int take)
{
    public List<T> Data { get; set; } = data;
    public int TotalCount { get; set; } = totalCount;
    public int Skip { get; set; } = skip;
    public int Take { get; set; } = take;
}
