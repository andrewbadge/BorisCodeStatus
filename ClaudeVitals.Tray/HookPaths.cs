namespace ClaudeVitals.Tray;

/// <summary>Locates the hook executable that ships alongside the tray app in the install folder.</summary>
internal static class HookPaths
{
    private const string HooksExeName = "ClaudeVitals.Hooks.exe";

    /// <summary>
    /// Full path to ClaudeVitals.Hooks.exe. The MSI installs it next to the tray executable, so the
    /// base directory is the answer in a real install; during development it may not exist yet,
    /// which callers detect with <see cref="HooksExecutableExists"/>.
    /// </summary>
    public static string HooksExecutable { get; } = Path.Combine(AppContext.BaseDirectory, HooksExeName);

    public static bool HooksExecutableExists => File.Exists(HooksExecutable);
}
