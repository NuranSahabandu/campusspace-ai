using CampusSpace.Api.Dtos.Auth;
using CampusSpace.Api.Dtos.Users;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    public const string InvalidCredentials = "Invalid email or password";

    /// <summary>Registers a Student account and signs it in.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var response = await auth.RegisterAsync(request, ct);
        return CreatedAtAction(nameof(UsersController.Get), "Users", new { id = response.User.Id }, response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var response = await auth.LoginAsync(request, ct);
        // One message for every failure, so callers cannot tell which emails exist.
        return response is null
            ? Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidCredentials)
            : Ok(response);
    }

    /// <summary>The signed-in user, reloaded from the database. Any role.</summary>
    [HttpGet("me")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var user = await auth.GetCurrentUserAsync(User.GetUserId(), ct);
        return user is null
            ? Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized")
            : Ok(user);
    }
}
