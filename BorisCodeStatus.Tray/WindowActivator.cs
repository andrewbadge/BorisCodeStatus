using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BorisCodeStatus.Tray;

/// <summary>
/// Finds running apps by process name and brings one to the front — what a click on the waiting
/// card does when <see cref="Core.State.ClickTargetPreference"/> names an app.
/// </summary>
internal static class WindowActivator
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    /// <summary>
    /// Apps with a visible main window right now, one entry per process name, for the menu.
    /// Label is the executable's description ("Google Chrome") where it can be read, else the
    /// process name. Read only when the menu opens; nothing is stored but the chosen name.
    /// </summary>
    public static IReadOnlyList<(string ProcessName, string Label)> RunningApps()
    {
        var self = Environment.ProcessId;
        var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == self || process.MainWindowHandle == IntPtr.Zero
                    || apps.ContainsKey(process.ProcessName))
                {
                    continue;
                }

                apps[process.ProcessName] = Describe(process);
            }
        }

        return [.. apps.Select(a => (a.Key, a.Value)).OrderBy(a => a.Value, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// Restores and focuses the first main window of the named process. Returns false when no
    /// such app is running. Called from the card's click, and the click is the user input that
    /// lets Windows allow the focus change.
    /// </summary>
    // ponytail: first window of that process wins, so with two terminal windows open it may not
    // be the one running Claude. Matching on the session's cwd in the window title would fix it.
    public static bool BringToFront(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                var window = process.MainWindowHandle;
                if (window == IntPtr.Zero)
                {
                    continue;
                }

                if (IsIconic(window))
                {
                    ShowWindow(window, SW_RESTORE);
                }

                return SetForegroundWindow(window);
            }
        }

        return false;
    }

    private static string Describe(Process process)
    {
        try
        {
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            return string.IsNullOrWhiteSpace(description) ? process.ProcessName : description.Trim();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Elevated or already-exited processes cannot be inspected; the name is enough.
            return process.ProcessName;
        }
    }
}
