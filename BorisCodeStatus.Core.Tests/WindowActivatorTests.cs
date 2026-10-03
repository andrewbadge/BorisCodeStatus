using BorisCodeStatus.Tray;

namespace BorisCodeStatus.Core.Tests;

/// <summary>Picking Claude's terminal window out of several by its title.</summary>
public class WindowActivatorTests
{
    [Theory]
    [InlineData("✳ Fix the login bug")]
    [InlineData("◐ Opening Claude terminal")]
    [InlineData("⠋ Running tests")]
    [InlineData("✻ Compacting")]
    public void ClaudeTitlesMatch(string title) => Assert.True(WindowActivator.LooksLikeClaude(title));

    [Theory]
    [InlineData("Windows PowerShell")]
    [InlineData("C:\\GitHub\\BorisCodeStatus\\bin\\Debug\\net10.0\\App.exe")]
    [InlineData("Administrator: Command Prompt")]
    [InlineData("~ ")]
    [InlineData("- bash")]
    [InlineData("✳")]
    [InlineData("")]
    public void OtherTitlesDoNot(string title) => Assert.False(WindowActivator.LooksLikeClaude(title));
}
