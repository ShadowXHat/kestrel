namespace Kestrel.Api.Auth;

/// <summary>Role names. Ordered by privilege: Viewer &lt; Analyst &lt; Admin.</summary>
public static class Roles
{
    public const string Viewer = "Viewer";
    public const string Analyst = "Analyst";
    public const string Admin = "Admin";

    /// <summary>All roles, in ascending privilege order.</summary>
    public static readonly string[] All = [Viewer, Analyst, Admin];
}

/// <summary>Authorization policy names (see Program.cs for their definitions).</summary>
public static class Policies
{
    public const string Viewer = "ViewerOnly";
    public const string Analyst = "AnalystOnly";
    public const string Admin = "AdminOnly";
}
