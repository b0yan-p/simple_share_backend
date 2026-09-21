using simpleShare_backend.Models.Application;

namespace simpleShare_backend.Services;

public interface IBalanceService
{
    Task<GroupBalances> GetGroupBalancesAsync(
        Guid groupId,
        Guid userId,
        CancellationToken ct = default
    );

    Task<OverallBalance> GetMyOverallBalanceAsync(
        Guid userId,
        CancellationToken ct = default
    );

    Task<(decimal paid, decimal owed, decimal net)>
    GetMemberNetInGroupAsync(
        Guid groupId,
        Guid memberId
    );
}
