using Microsoft.AspNetCore.Mvc;
using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Services;

namespace simpleShare_backend.Controllers
{
    // Errors are AppExceptions answered by GlobalExceptionHandler, and invalid
    // bodies never get here - [ApiController] answers validation_error.
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController(IAuthService authService) : ControllerBase
    {
        private readonly IAuthService _authService = authService;

        [HttpPost("register")]
        public async Task<ActionResult<LoginUserResponse>> RegisterAsync([FromBody] RegisterRequest input) =>
            Ok(await _authService.RegisterAsync(input));

        [HttpPost("login")]
        public async Task<ActionResult<LoginUserResponse>> LoginAsync([FromBody] LoginRequest input) =>
            Ok(await _authService.LoginAsync(input));

        [HttpPost("google")]
        public async Task<ActionResult<LoginUserResponse>> GoogleLoginAsync([FromBody] GoogleLoginRequest input, CancellationToken ct) =>
            Ok(await _authService.GoogleLoginAsync(input.IdToken, ct));
    }
}
