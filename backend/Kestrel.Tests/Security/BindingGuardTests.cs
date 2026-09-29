using Kestrel.Api.Security;
using Xunit;

namespace Kestrel.Tests.Security;

/// <summary>
/// Phase 1 binding-guard logic: loopback by default, network binding only with
/// the explicit override, fail-closed on unparseable targets.
/// </summary>
public class BindingGuardTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5000", true)]
    [InlineData("http://localhost:5000", true)]
    [InlineData("http://LOCALHOST:5000", true)]
    [InlineData("http://[::1]:5000", true)]
    [InlineData("https://127.0.0.1:5001", true)]
    [InlineData("http://0.0.0.0:5000", false)]
    [InlineData("http://+:5000", false)]
    [InlineData("http://*:5000", false)]
    [InlineData("http://192.168.1.10:5000", false)]
    [InlineData("http://analyst-workstation:5000", false)]
    [InlineData("not a url", false)]
    public void IsLoopbackTarget_ClassifiesCorrectly(string url, bool expected)
    {
        Assert.Equal(expected, BindingGuard.IsLoopbackTarget(url));
    }

    [Fact]
    public void SplitUrls_HandlesSemicolonsAndWhitespace()
    {
        var urls = BindingGuard.SplitUrls(" http://127.0.0.1:5000 ;  http://[::1]:5001 ;; ");
        Assert.Equal(new[] { "http://127.0.0.1:5000", "http://[::1]:5001" }, urls);
    }

    [Fact]
    public void SplitUrls_EmptyInput_YieldsEmpty()
    {
        Assert.Empty(BindingGuard.SplitUrls(null));
        Assert.Empty(BindingGuard.SplitUrls(""));
        Assert.Empty(BindingGuard.SplitUrls("   "));
    }

    [Fact]
    public void Evaluate_LoopbackOnly_IsAllowedWithoutOverride()
    {
        var decision = BindingGuard.Evaluate(new[] { "http://127.0.0.1:5000" }, allowNetworkBinding: false);

        Assert.True(decision.IsAllowed);
        Assert.Empty(decision.NonLoopbackUrls);
        Assert.Empty(decision.Reasons);
    }

    [Fact]
    public void Evaluate_NetworkBinding_WithoutOverride_IsRefused_WithReasons()
    {
        var decision = BindingGuard.Evaluate(
            new[] { "http://127.0.0.1:5000", "http://0.0.0.0:5000" },
            allowNetworkBinding: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal(new[] { "http://0.0.0.0:5000" }, decision.NonLoopbackUrls);
        var reason = Assert.Single(decision.Reasons);
        Assert.Contains("KestrelApp:AllowNetworkBinding=true", reason);
    }

    [Fact]
    public void Evaluate_NetworkBinding_WithOverride_IsAllowed_AndStillFlagged()
    {
        var decision = BindingGuard.Evaluate(
            new[] { "http://0.0.0.0:5000" },
            allowNetworkBinding: true);

        Assert.True(decision.IsAllowed);
        Assert.Equal(new[] { "http://0.0.0.0:5000" }, decision.NonLoopbackUrls);
    }

    [Fact]
    public void Evaluate_MixedUrls_OnlyOffendingOnesAreFlagged()
    {
        var decision = BindingGuard.Evaluate(
            new[] { "http://localhost:5000", "http://192.168.1.10:5000", "http://[::1]:5001" },
            allowNetworkBinding: false);

        Assert.False(decision.IsAllowed);
        var offending = Assert.Single(decision.NonLoopbackUrls);
        Assert.Equal("http://192.168.1.10:5000", offending);
    }
}
