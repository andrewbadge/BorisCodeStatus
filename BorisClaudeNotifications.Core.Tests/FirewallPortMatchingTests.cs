using BorisClaudeNotifications.Tray;

namespace BorisClaudeNotifications.Core.Tests;

/// <summary>
/// Covers matching a firewall rule's LocalPorts against the relay's port.
///
/// This decides whether the tray tells the user they need an administrator. Reading it too
/// loosely claims the display can reach the PC when it cannot; too strictly sends someone to an
/// elevation prompt they did not need. The COM value is free-form, so every shape Windows
/// actually emits is pinned down here.
/// </summary>
public class FirewallPortMatchingTests
{
    private const int RelayPort = 5080;

    [Theory]
    [InlineData("5080", "exact single port")]
    [InlineData(" 5080 ", "padded single port")]
    [InlineData("*", "any port")]
    [InlineData(" * ", "any port, padded")]
    [InlineData("80,5080,8080", "comma-separated list")]
    [InlineData("80, 5080", "list with spaces")]
    [InlineData("5000-6000", "range covering it")]
    [InlineData("5080-5080", "single-value range")]
    [InlineData("80,5000-6000", "list containing a range")]
    public void CoversThePort(string localPorts, string shape)
    {
        Assert.True(FirewallGuard.PortsInclude(localPorts, RelayPort), shape);
    }

    [Theory]
    [InlineData("5079", "a different port")]
    [InlineData("80,8080", "a list without it")]
    [InlineData("5081-6000", "a range starting above it")]
    [InlineData("1-5079", "a range ending below it")]
    public void DoesNotCoverOtherPorts(string localPorts, string shape)
    {
        Assert.False(FirewallGuard.PortsInclude(localPorts, RelayPort), shape);
    }

    /// <summary>
    /// Unparseable input must fail towards "no rule found". That costs a needless warning, where
    /// the opposite would assure the user of reachability they do not have — and the display
    /// would simply never update, which is the failure this whole guard exists to prevent.
    /// </summary>
    [Theory]
    [InlineData(null, "null")]
    [InlineData("", "empty")]
    [InlineData("   ", "whitespace")]
    [InlineData("RPC", "the keyword Windows uses for dynamic ports")]
    [InlineData("abc-def", "an unparseable range")]
    [InlineData("5000-", "a truncated range")]
    [InlineData(",,", "separators only")]
    public void FailsSafeOnAnythingItCannotRead(string? localPorts, string shape)
    {
        Assert.False(FirewallGuard.PortsInclude(localPorts, RelayPort), shape);
    }

    /// <summary>A custom BORISCLAUDENOTIFICATIONS_PORT has to be matched, not a hardcoded 5080.</summary>
    [Fact]
    public void MatchesWhicheverPortIsAskedFor()
    {
        Assert.True(FirewallGuard.PortsInclude("9000", 9000));
        Assert.False(FirewallGuard.PortsInclude("5080", 9000));
        Assert.True(FirewallGuard.PortsInclude("*", 9000));
    }
}
