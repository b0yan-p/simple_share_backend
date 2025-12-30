using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using simpleShare_backend.Contracts.Auth;
using simpleShare_backend.Interfaces;
using simpleShare_backend.Models.Identity;

namespace simpleShare_backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController(UserManager<User> userManager, ITokenService tokenService) : ControllerBase
    {
        private readonly UserManager<User> _userManager = userManager;
        private readonly ITokenService _tokenService = tokenService;

        [HttpPost("register")]
        public async Task<IActionResult> RegisterAsync([FromBody] RegisterRequest input)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var user = new User
                {
                    FirstName = input.FirstName,
                    LastName = input.LastName,
                    Email = input.Email,
                    UserName = input.Email
                };

                if (input.Password != input.RepeatPassword)
                    return BadRequest("Passwords does not matches");


                var createUser = await _userManager.CreateAsync(user, input.Password);
                if (!createUser.Succeeded) return BadRequest(createUser.Errors);

                var roleResult = await _userManager.AddToRoleAsync(user, "User");
                if (!roleResult.Succeeded)
                    return StatusCode((int)HttpStatusCode.InternalServerError, roleResult.Errors);

                var roles = await _userManager.GetRolesAsync(user);
                return Ok(
                    new LoginUserResponse
                    {
                        Id = user.Id,
                        Email = user.Email!,
                        FirstName = user.FirstName,
                        LastName = user.LastName,
                        Token = _tokenService.CreateToken(user, roles)
                    }
                );
            }
            catch (Exception e)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, e.Message);
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> LoginAsync([FromBody] LoginRequest input)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var user = await _userManager.FindByEmailAsync(input.Email);
                if (user is null)
                    return Unauthorized("Invalid email or password");


                var isPasswordValid = await _userManager.CheckPasswordAsync(user, input.Password);
                if (!isPasswordValid)
                    return Unauthorized("Invalid email or password");

                var userRole = await _userManager.GetRolesAsync(user);
                var token = _tokenService.CreateToken(user, userRole);

                return Ok(
                    new LoginUserResponse
                    {
                        Id = user.Id,
                        Email = user.Email!,
                        FirstName = user.FirstName,
                        LastName = user.LastName,
                        Token = token
                    }
                );
            }
            catch (Exception e)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, e.Message);
            }
        }
    }
}
