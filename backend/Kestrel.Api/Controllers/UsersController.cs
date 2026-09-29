using System.ComponentModel.DataAnnotations;
using Kestrel.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kestrel.Api.Controllers;

/// <summary>
/// User administration (Admin only, enforced server-side via the AdminOnly policy).
///
/// This is the ONLY place accounts are created — there is no open self-signup
/// (TECH_STACK.md §2.9). Guards:
///   - The last administrator can never be demoted or deleted (no bricking).
///   - An administrator cannot delete their own account.
///   - Identity rule violations are surfaced per-rule, never silently.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.Admin)]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly ILogger<UsersController> _logger;

    public UsersController(UserManager<AppUser> userManager, ILogger<UsersController> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public sealed record CreateUserRequest(
        [Required, StringLength(64, MinimumLength = 2)] string Username,
        [Required, StringLength(256, MinimumLength = 1)] string Password,
        string[]? Roles);

    public sealed record ReplaceRolesRequest([Required] string[] Roles);

    public sealed record UserDto(
        string Id,
        string Username,
        IReadOnlyList<string> Roles,
        DateTimeOffset? LockoutEndUtc,
        int AccessFailedCount);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var users = await _userManager.Users
            .OrderBy(u => u.UserName)
            .ToListAsync(cancellationToken);

        var result = new List<UserDto>(users.Count);
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            result.Add(new UserDto(user.Id, user.UserName!, roles.ToArray(), user.LockoutEnd, user.AccessFailedCount));
        }

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var requestedRoles = request.Roles is { Length: > 0 }
            ? request.Roles.Distinct(StringComparer.Ordinal).ToArray()
            : new[] { Auth.Roles.Viewer };

        var invalidRoles = requestedRoles.Where(r => !Auth.Roles.All.Contains(r)).ToArray();
        if (invalidRoles.Length > 0)
        {
            return BadRequest(new { errors = new[] { $"Unknown roles: {string.Join(", ", invalidRoles)}" } });
        }

        var user = new AppUser { UserName = request.Username };
        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            // Identity password/username rules — surfaced per-rule, no silent failure.
            _logger.LogWarning("Audit: user creation failed for '{Username}': {Errors}",
                request.Username, string.Join("; ", createResult.Errors.Select(e => e.Description)));
            return BadRequest(new { errors = createResult.Errors.Select(e => e.Description) });
        }

        var roleResult = await _userManager.AddToRolesAsync(user, requestedRoles);
        if (!roleResult.Succeeded)
        {
            _logger.LogError("Audit: user '{Username}' ({UserId}) was created but role assignment failed: {Errors}",
                user.UserName, user.Id, string.Join("; ", roleResult.Errors.Select(e => e.Description)));
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { error = "user_created_but_role_assignment_failed", errors = roleResult.Errors.Select(e => e.Description) });
        }

        _logger.LogInformation("Audit: user '{Username}' ({UserId}) created by {Actor} with roles [{Roles}]",
            user.UserName, user.Id, User.Identity?.Name, string.Join(", ", requestedRoles));

        return StatusCode(StatusCodes.Status201Created,
            new UserDto(user.Id, user.UserName!, requestedRoles, null, 0));
    }

    [HttpPut("{id}/roles")]
    public async Task<IActionResult> ReplaceRoles(string id, [FromBody] ReplaceRolesRequest request)
    {
        var target = await _userManager.FindByIdAsync(id);
        if (target is null)
        {
            return NotFound(new { error = "user_not_found" });
        }

        var newRoles = request.Roles.Distinct(StringComparer.Ordinal).ToArray();
        var invalidRoles = newRoles.Where(r => !Auth.Roles.All.Contains(r)).ToArray();
        if (invalidRoles.Length > 0)
        {
            return BadRequest(new { errors = new[] { $"Unknown roles: {string.Join(", ", invalidRoles)}" } });
        }

        // Guard: never strip the last administrator.
        if (await _userManager.IsInRoleAsync(target, Auth.Roles.Admin) && !newRoles.Contains(Auth.Roles.Admin))
        {
            var otherAdmins = (await _userManager.GetUsersInRoleAsync(Auth.Roles.Admin)).Count(u => u.Id != target.Id);
            if (otherAdmins == 0)
            {
                _logger.LogWarning("Audit: refused to demote the last administrator '{Username}' (requested by {Actor})",
                    target.UserName, User.Identity?.Name);
                return Conflict(new
                {
                    error = "last_admin",
                    detail = "Refusing to remove the only administrator account. Create another admin first."
                });
            }
        }

        var currentRoles = (await _userManager.GetRolesAsync(target)).ToHashSet(StringComparer.Ordinal);

        // Apply additions first — a failure leaves the account unchanged.
        var toAdd = newRoles.Where(r => !currentRoles.Contains(r)).ToArray();
        if (toAdd.Length > 0)
        {
            var addResult = await _userManager.AddToRolesAsync(target, toAdd);
            if (!addResult.Succeeded)
            {
                return BadRequest(new { errors = addResult.Errors.Select(e => e.Description) });
            }
        }

        var toRemove = currentRoles.Where(r => !newRoles.Contains(r, StringComparer.Ordinal)).ToArray();
        if (toRemove.Length > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(target, toRemove);
            if (!removeResult.Succeeded)
            {
                _logger.LogError("Audit: role removal failed for '{Username}': {Errors}",
                    target.UserName, string.Join("; ", removeResult.Errors.Select(e => e.Description)));
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "role_removal_failed", errors = removeResult.Errors.Select(e => e.Description) });
            }
        }

        var finalRoles = await _userManager.GetRolesAsync(target);
        _logger.LogInformation("Audit: roles for '{Username}' ({UserId}) set to [{Roles}] by {Actor}",
            target.UserName, target.Id, string.Join(", ", finalRoles), User.Identity?.Name);

        return Ok(new UserDto(target.Id, target.UserName!, finalRoles.ToArray(), target.LockoutEnd, target.AccessFailedCount));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var target = await _userManager.FindByIdAsync(id);
        if (target is null)
        {
            return NotFound(new { error = "user_not_found" });
        }

        if (string.Equals(target.Id, _userManager.GetUserId(User), StringComparison.Ordinal))
        {
            return Conflict(new
            {
                error = "self_delete",
                detail = "You cannot delete your own account. Ask another administrator to do it."
            });
        }

        // Guard: never delete the last administrator.
        if (await _userManager.IsInRoleAsync(target, Auth.Roles.Admin))
        {
            var otherAdmins = (await _userManager.GetUsersInRoleAsync(Auth.Roles.Admin)).Count(u => u.Id != target.Id);
            if (otherAdmins == 0)
            {
                _logger.LogWarning("Audit: refused to delete the last administrator '{Username}' (requested by {Actor})",
                    target.UserName, User.Identity?.Name);
                return Conflict(new { error = "last_admin", detail = "Refusing to delete the only administrator account." });
            }
        }

        var deleteResult = await _userManager.DeleteAsync(target);
        if (!deleteResult.Succeeded)
        {
            return BadRequest(new { errors = deleteResult.Errors.Select(e => e.Description) });
        }

        _logger.LogInformation("Audit: user '{Username}' ({UserId}) deleted by {Actor}",
            target.UserName, target.Id, User.Identity?.Name);
        return NoContent();
    }

    [HttpPost("{id}/unlock")]
    public async Task<IActionResult> Unlock(string id)
    {
        var target = await _userManager.FindByIdAsync(id);
        if (target is null)
        {
            return NotFound(new { error = "user_not_found" });
        }

        var clearResult = await _userManager.SetLockoutEndDateAsync(target, null);
        if (!clearResult.Succeeded)
        {
            return BadRequest(new { errors = clearResult.Errors.Select(e => e.Description) });
        }

        await _userManager.ResetAccessFailedCountAsync(target);
        _logger.LogInformation("Audit: user '{Username}' unlocked by {Actor}", target.UserName, User.Identity?.Name);
        return NoContent();
    }
}
