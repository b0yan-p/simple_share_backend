using Microsoft.AspNetCore.Mvc;
using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Services;

namespace simpleShare_backend.Controllers
{
    // Errors are AppExceptions answered by GlobalExceptionHandler, and invalid
    // bodies never get here - [ApiController] answers validation_error.
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController(
        IAuthService authService,
        IEmailVerificationService emailVerificationService
    ) : ControllerBase
    {
        private readonly IAuthService _authService = authService;
        private readonly IEmailVerificationService _emailVerificationService = emailVerificationService;

        [HttpPost("register")]
        public async Task<ActionResult<RegisterResponse>> RegisterAsync([FromBody] RegisterRequest input) =>
            Ok(await _authService.RegisterAsync(input));

        // Always 200, whatever the address turns out to be - see ResendAsync.
        [HttpPost("resend-verification")]
        public async Task<IActionResult> ResendVerificationAsync(
            [FromBody] ResendVerificationRequest input, CancellationToken ct)
        {
            await _emailVerificationService.ResendAsync(input.Email, ct);
            return Ok(new { sent = true });
        }

        // POST, not GET: the link in the email opens a page, and the page sends
        // this. A mail scanner that prefetches the link must not confirm an
        // address nobody has clicked on.
        [HttpPost("confirm-email")]
        public async Task<ActionResult<ConfirmEmailResponse>> ConfirmEmailAsync([FromBody] ConfirmEmailRequest input) =>
            Ok(await _emailVerificationService.ConfirmAsync(input));

        [HttpPost("login")]
        public async Task<ActionResult<LoginUserResponse>> LoginAsync([FromBody] LoginRequest input) =>
            Ok(await _authService.LoginAsync(input));

        [HttpPost("google")]
        public async Task<ActionResult<LoginUserResponse>> GoogleLoginAsync([FromBody] GoogleLoginRequest input, CancellationToken ct) =>
            Ok(await _authService.GoogleLoginAsync(input.IdToken, ct));
    }
}
