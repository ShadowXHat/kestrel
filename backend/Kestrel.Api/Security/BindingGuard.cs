using System.Net;
using Microsoft.Extensions.Configuration;

namespace Kestrel.Api.Security;

/// <summary>
/// Phase 1 security gate: Kestrel binds to loopback only, unless the operator
/// explicitly opts in. Going network-facing additionally requires TLS (Phase 7)
/// and the nginx TLS-terminating proxy (Phase 12) — the guard warns loudly.
/// This is a config guard, not a documentation note (WHY_THIS_STACK.md §17).
/// </summary>
public static class BindingGuard
{
    /// <summary>
    /// Evaluates the startup binding configuration and throws (fail fast, before
    /// anything binds) when a non-loopback address is configured without the
    /// explicit <c>KestrelApp:AllowNetworkBinding</c> override.
    /// </summary>
    public static void Enforce(IConfiguration configuration, ILogger logger)
    {
        var raw = ResolveConfiguredUrls(configuration);
        if (raw is null)
        {
            logger.LogInformation(
                "Binding guard: no explicit URLs configured; Kestrel defaults apply (loopback only).");
            return;
        }

        var urls = SplitUrls(raw);
        var allowNetworkBinding = configuration.GetValue("KestrelApp:AllowNetworkBinding", false);
        var decision = Evaluate(urls, allowNetworkBinding);

        if (!decision.IsAllowed)
        {
            var reason = string.Join(" ", decision.Reasons);
            logger.LogError("Binding guard: startup refused. {Reason}", reason);
            throw new InvalidOperationException(reason);
        }

        foreach (var url in urls)
        {
            var scope = decision.NonLoopbackUrls.Contains(url) ? "NETWORK-FACING" : "loopback";
            logger.LogInformation("Binding guard: {Url} ({Scope})", url, scope);
        }

        if (decision.NonLoopbackUrls.Count > 0)
        {
            logger.LogWarning(
                "Binding guard: NETWORK-FACING bind configured while TLS is not yet enforced (Phase 7). " +
                "Do not expose this instance until HTTPS terminates in front of it (nginx, Phase 12).");
        }
    }

    /// <summary>
    /// Resolves the effective bind URLs following Kestrel's own precedence:
    /// ASPNETCORE_URLS (env) &gt; DOTNET_URLS (env) &gt; "Urls" key (cmdline/appsettings)
    /// &gt; Kestrel:Endpoints:{name}:Url.
    /// </summary>
    public static string? ResolveConfiguredUrls(IConfiguration configuration)
    {
        var fromEnv = configuration["ASPNETCORE_URLS"];
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var fromDotNetEnv = configuration["DOTNET_URLS"];
        if (!string.IsNullOrWhiteSpace(fromDotNetEnv))
        {
            return fromDotNetEnv;
        }

        var fromConfig = configuration["Urls"];
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig;
        }

        var endpoints = configuration.GetSection("Kestrel:Endpoints");
        if (endpoints.Exists())
        {
            var urls = endpoints.GetChildren()
                .Select(child => child["Url"])
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Cast<string>()
                .ToList();

            if (urls.Count > 0)
            {
                return string.Join(";", urls);
            }
        }

        return null;
    }

    /// <summary>Splits a "url;url" string the way ASP.NET Core does.</summary>
    public static string[] SplitUrls(string? raw)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Pure decision logic (unit-tested in Kestrel.Tests).</summary>
    public static BindingDecision Evaluate(IReadOnlyList<string> urls, bool allowNetworkBinding)
    {
        var offending = new List<string>();
        foreach (var url in urls)
        {
            if (!IsLoopbackTarget(url))
            {
                offending.Add(url);
            }
        }

        var reasons = new List<string>();
        if (offending.Count > 0 && !allowNetworkBinding)
        {
            reasons.Add(
                $"Refusing to bind to non-loopback addresses: {string.Join(", ", offending)}. " +
                "Kestrel binds to loopback only by default (Phase 1 security gate). To bind to a network interface, " +
                "set KestrelApp:AllowNetworkBinding=true (env: KestrelApp__AllowNetworkBinding=true). Note that " +
                "network-facing deployment additionally requires TLS (Phase 7) and is meant to run behind the " +
                "nginx TLS-terminating proxy (Phase 12).");
        }

        return new BindingDecision(
            IsAllowed: offending.Count == 0 || allowNetworkBinding,
            NonLoopbackUrls: offending,
            Reasons: reasons);
    }

    /// <summary>
    /// Conservative loopback classification: only "localhost" and parseable
    /// loopback IPs qualify. Unparseable targets are treated as network-facing
    /// (fail closed).
    /// </summary>
    public static bool IsLoopbackTarget(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            return IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip);
        }

        var host = uri.Host.TrimEnd('.');
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Result of <see cref="BindingGuard.Evaluate"/>.</summary>
public sealed record BindingDecision(
    bool IsAllowed,
    IReadOnlyList<string> NonLoopbackUrls,
    IReadOnlyList<string> Reasons);
