using System.ComponentModel.DataAnnotations;
using Kestrel.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kestrel.Api.Controllers;

/// <summary>
/// Cookie-session authentication endpoints.
///
/// Rules enforced here (TECH_STACK.md §2.9 / WHY_THIS_STACK.md §9):
///  - No open self-signup: accounts are created by Admins via /api/users.
///    The only bootstrap path is the single-admin first-run seed.
///  - Failures are deliberately generic ("invalid_credentials") so callers
///    cannot enumerate usernames or lockout state; specifics go to the
///    Serilog audit trail instead.
///  - Login is rate-limited and lockout is enabled on failure.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly SignInManager<AppUser> _signInManager;
    private readonly UserManager<AppUser> _userManager;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        SignInManager<AppUser> signInManager,
        UserManager<AppUser> userManager,
        ILogger<AuthController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    public sealed record LoginRequest(
        [Required, StringLength(64, MinimumLength = 1)] string Username,
        [Required, StringLength(256, MinimumLength = 1)] string Password);

    public sealed record ChangePasswordRequest(
        [Required, StringLength(256, MinimumLength = 1)] string CurrentPassword,
        [Required, StringLength(256, MinimumLength = 1)] string NewPassword);

    public sealed record SessionResponse(string Id, string Username, IReadOnlyList<string> Roles);

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var user = await _userManager.FindByNameAsync(request.Username);

        if (user is null)
        {
            _logger.LogWarning("Audit: login failed (unknown user) for {Username} from {RemoteIp}",
                request.Username, remoteIp);
            return Unauthorized(new { error = "invalid_credentials" });
        }

        var result = await _signInManager.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var roles = await _userManager.GetRolesAsync(user);
            _logger.LogInformation("Audit: login succeeded for {Username} ({UserId}) from {RemoteIp}",
                user.UserName, user.Id, remoteIp);
            return Ok(new SessionResponse(user.Id, user.UserName!, roles.ToArray()));
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("Audit: account {Username} locked out after repeated failures (from {RemoteIp})",
                user.UserName, remoteIp);
        }
        else
        {
            _logger.LogWarning("Audit: login failed (bad password) for {Username} from {RemoteIp}",
                user.UserName, remoteIp);
        }

        // Generic on the wire — no account-existence or lockout-state disclosure.
        return Unauthorized(new { error = "invalid_credentials" });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var username = User.Identity?.Name;
        await _signInManager.SignOutAsync();
        _logger.LogInformation("Audit: logout for {Username}", username);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized(new { error = "no_session" });
        }

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(new SessionResponse(user.Id, user.UserName!, roles.ToArray()));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized(new { error = "no_session" });
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            // Per-rule error surfacing — never a silent failure.
            var errors = result.Errors.Select(e => e.Description).ToArray();
            _logger.LogWarning("Audit: password change failed for {Username}: {Errors}",
                user.UserName, string.Join("; ", errors));
            return BadRequest(new { errors });
        }

        // ChangePasswordAsync already rotated the security stamp; ending the
        // current session forces re-authentication with the new password.
        await _signInManager.SignOutAsync();
        _logger.LogInformation("Audit: password changed for {Username}; session ended", user.UserName);
        return NoContent();
    }
}

