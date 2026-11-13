using simpleShare_backend.Models.Domain;

namespace simpleShare_backend.Services;


public interface IBetaSignupService
{
  Task JoinBetaAsync(string email, CancellationToken cancellationToken = default);
}
