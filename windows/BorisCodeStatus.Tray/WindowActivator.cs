using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BorisCodeStatus.Tray;

/// <summary>
/// Finds running apps by process name and brings one to the front â€” what a click on the waiting
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

    private const uint GW_OWNER = 4;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>
    /// Apps with a visible, titled, unowned top-level window right now â€” what Alt-Tab would show â€”
    /// one entry per process name. Label is the executable's description ("Google Chrome") where it
    /// can be read, else the process name. Nothing is stored but the name the user picks.
    ///
    /// One pass over the top-level windows, then only those processes are opened. Asking every
    /// process for its <see cref="Process.MainWindowHandle"/> instead enumerates every window once
    /// per process, which took about a second. Still slow enough to keep off the UI thread.
    /// </summary>
    public static IReadOnlyList<(string ProcessName, string Label)> RunningApps()
    {
        var self = (uint)Environment.ProcessId;
        var processIds = new HashSet<uint>();
        EnumWindows(
            (window, _) =>
            {
                if (IsWindowVisible(window) && GetWindow(window, GW_OWNER) == IntPtr.Zero && GetWindowTextLength(window) > 0)
                {
                    GetWindowThreadProcessId(window, out var processId);
                    if (processId != self)
                    {
                        processIds.Add(processId);
                    }
                }

                return true;
            },
            IntPtr.Zero);

        var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var processId in processIds)
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (!apps.ContainsKey(process.ProcessName))
                {
                    apps[process.ProcessName] = Describe(process);
                }
            }
            catch (ArgumentException)
            {
                // Exited since its window was seen.
            }
        }

        return [.. apps.Select(a => (a.Key, a.Value)).OrderBy(a => a.Value, StringComparer.CurrentCultureIgnoreCase)];
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, char[] text, int maxCount);

    /// <summary>
    /// Restores and focuses a window of the named process, preferring one whose title looks like
    /// Claude Code's (<see cref="LooksLikeClaude"/>) — so with Visual Studio's debug console open
    /// beside it, the click still lands on Claude. Returns false when no such app is running.
    /// Called from the card's click, and the click is the user input that lets Windows allow the
    /// focus change.
    /// </summary>
    public static bool BringToFront(string processName)
    {
        var processIds = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            processIds.Add((uint)process.Id);
            process.Dispose();
        }

        var windows = new List<(IntPtr Window, string Title)>();
        EnumWindows(
            (window, _) =>
            {
                GetWindowThreadProcessId(window, out var processId);
                if (processIds.Contains(processId) && IsWindowVisible(window) && GetWindow(window, GW_OWNER) == IntPtr.Zero)
                {
                    var buffer = new char[512];
                    var length = GetWindowText(window, buffer, buffer.Length);
                    if (length > 0)
                    {
                        windows.Add((window, new string(buffer, 0, length)));
                    }
                }

                return true;
            },
            IntPtr.Zero);

        if (windows.Count == 0)
        {
            return false;
        }

        // Enumeration is in Z order, so with no Claude-looking title the most recently used wins.
        var target = windows.FirstOrDefault(w => LooksLikeClaude(w.Title)).Window;
        if (target == IntPtr.Zero)
        {
            target = windows[0].Window;
        }

        if (IsIconic(target))
        {
            ShowWindow(target, SW_RESTORE);
        }

        return SetForegroundWindow(target);
    }

    /// <summary>
    /// Claude Code titles its terminal with the conversation's topic behind a status glyph
    /// ("✳ Fix the login bug", "◐ Opening Claude terminal"); a shell or a debug console shows a
    /// name or a path. Observed, not documented — if Claude Code changes its title format this
    /// stops matching and the click falls back to the front-most window.
    /// </summary>
    // ponytail: two Claude sessions in separate windows → the front-most one wins.
    internal static bool LooksLikeClaude(string title) =>
        title.Length > 2
        && title[1] == ' '
        && !char.IsLetterOrDigit(title[0])
        && !char.IsWhiteSpace(title[0])
        && title[0] is not ('"' or '\'' or '(' or '[' or '-' or '.' or '\\' or '/' or '~');

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, char[] path, ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// The executable's description, read from its file. The path comes from
    /// QueryFullProcessImageName rather than <see cref="Process.MainModule"/>, which walks the whole
    /// module list of the target and was most of a second for a dozen apps. Limited-information
    /// access also works on elevated processes, which MainModule cannot open.
    /// </summary>
    private static string Describe(Process process)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
        if (handle == IntPtr.Zero)
        {
            return process.ProcessName;
        }

        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size))
            {
                return process.ProcessName;
            }

            var description = FileVersionInfo.GetVersionInfo(new string(buffer, 0, (int)size)).FileDescription;
            return string.IsNullOrWhiteSpace(description) ? process.ProcessName : description.Trim();
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            return process.ProcessName;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
