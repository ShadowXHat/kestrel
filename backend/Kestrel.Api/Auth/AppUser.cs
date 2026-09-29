using Microsoft.AspNetCore.Identity;

namespace Kestrel.Api.Auth;

/// <summary>
/// Application user. Phase 1 keeps it minimal; forward-compatible additions
/// (display name, per-user preferences) come with later phases.
/// </summary>
public class AppUser : IdentityUser
{
}
