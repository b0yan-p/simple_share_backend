
using Microsoft.AspNetCore.Mvc;
using simpleShare_backend.Services;

[ApiController]
[Route("api/beta-signups")]
public class BetaSignupController(IBetaSignupService betaSignupService) : ControllerBase
{
  private readonly IBetaSignupService _betaSignupService = betaSignupService;

  [HttpPost]
  public async Task<IActionResult> JoinBeta(
      [FromBody] JoinBetaRequest request,
      CancellationToken cancellationToken
  )
  {
    const string message =
        "Your beta signup request has been received.";

    // Honeypot: silently ignore likely bot submissions.
    if (!string.IsNullOrWhiteSpace(request.Name))
    {
      return Ok(new { message });
    }

    await _betaSignupService.JoinBetaAsync(
        request.Email,
        cancellationToken);

    return Ok(new { message });
  }
}
