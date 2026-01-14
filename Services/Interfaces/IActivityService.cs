using simpleShare_backend.Common;
using simpleShare_backend.Contracts.Activity;

namespace simpleShare_backend.Services.Interfaces;

public interface IActivityService
{
    Task<PageResponse<ActivityResponse>> GetMyActivitiesAsync(
        Guid userId,
        int skip,
        int take,
        CancellationToken ct = default
    );
}
