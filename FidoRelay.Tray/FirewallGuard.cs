using System.Diagnostics;
using System.Reflection;
using System.Text;
using FidoRelay.Core.State;

namespace FidoRelay.Tray;

/// <summary>What the Windows Firewall currently does to inbound traffic for this executable.</summary>
internal enum FirewallState
{
    /// <summary>Could not be determined — treat as "probably fine", never nag the user about it.</summary>
    Unknown,

    /// <summary>An enabled Allow rule matches this exe on the active profile.</summary>
    Allowed,

    /// <summary>
    /// An enabled Block rule matches. This is what Windows creates when a non-administrator
    /// dismisses or cancels the firewall prompt — it does not simply skip the rule.
    /// </summary>
    Blocked,

    /// <summary>No rule matches at all. Default inbound behaviour applies, which means blocked.</summary>
    NoRule,
}

/// <summary>The detected state plus the profile it was judged against, for display.</summary>
internal sealed record FirewallVerdict(FirewallState State, string ProfileName)
{
    /// <summary>True when the ESP32 is unlikely to be able to reach the endpoint.</summary>
    public bool BlocksTheDisplay => State is FirewallState.Blocked or FirewallState.NoRule;

    /// <summary>True when allowing traffic would open the endpoint on a network Windows calls public.</summary>
    public bool IsPublicNetwork => ProfileName.Equals("Public", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Explains, detects and repairs the one part of this app that a non-administrator cannot complete
/// on their own: the inbound firewall rule the ESP32 needs.
///
/// Installing needs no admin rights, but binding a non-loopback port raises a firewall prompt that
/// only an administrator can approve. A non-admin can only dismiss it — and dismissing does not
/// leave the firewall untouched, it writes Block rules. Everything then still works on localhost,
/// so the failure is invisible until the display never updates.
///
/// Reading firewall rules needs no elevation, so detection runs in-process. Changing them does,
/// so repair is handed to an elevated PowerShell (or to the clipboard, for a user who has to ask
/// their IT team).
/// </summary>
internal static class FirewallGuard
{
    // INetFwPolicy2 profile bitmask.
    private const int ProfileDomain = 1;
    private const int ProfilePrivate = 2;
    private const int ProfilePublic = 4;

    // NET_FW_ACTION / NET_FW_RULE_DIRECTION.
    private const int ActionAllow = 1;

    /// <summary>IANA protocol number for TCP, as the firewall COM API reports it.</summary>
    private const int ProtocolTcp = 6;
    private const int DirectionInbound = 1;

    /// <summary>Matches VitalsApiOptions.Port; only used when a caller does not supply one.</summary>
    private const int DefaultPort = 5080;

    private const string RuleDisplayName = "FidoRelay";

    /// <summary>The rule name used before the rename, cleaned up when the fix script runs.</summary>
    private const string LegacyRuleDisplayName = "Claude Vitals Relay";

    /// <summary>Marker file so the pre-bind explanation is shown once per machine, not every login.</summary>
    private static string NoticeShownFlag => Path.Combine(VitalsPaths.DataDirectory, "firewall-notice-shown");

    private static string TrayExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "FidoRelay.Tray.exe");

    /// <summary>
    /// Shown once, immediately before the API binds — so the user reads the explanation *before*
    /// the Windows prompt appears, rather than meeting an unexplained dialog and dismissing it.
    /// That dismissal is exactly the failure this is trying to prevent.
    /// </summary>
    public static void ShowFirstRunNoticeIfNeeded(int port = DefaultPort)
    {
        try
        {
            if (File.Exists(NoticeShownFlag))
            {
                return;
            }

            // Nothing to warn about if the rule is already in place (for example on a reinstall).
            if (Detect(port).State == FirewallState.Allowed)
            {
                MarkNoticeShown();
                return;
            }

            MessageBox.Show(
                "Windows is about to ask whether to allow FidoRelay through the firewall.\r\n\r\n" +
                "Please choose \"Allow access\".\r\n\r\n" +
                "Why it matters: the relay serves usage data to your ESP32 display over the local " +
                "network. Without this rule the display can never reach it — everything will look " +
                "fine on this PC, but the display will simply never update.\r\n\r\n" +
                "If the prompt is dismissed, Windows does not just skip it — it records a rule that " +
                "blocks the relay. Approving the prompt needs administrator rights. If you do not " +
                "have them, dismiss it and use \"Fix firewall access\" in the tray menu, which can " +
                "give you the exact command to send to your IT team.",
                "FidoRelay — firewall prompt incoming",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            MarkNoticeShown();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A notice we failed to record is not worth blocking startup over.
        }
    }

    private static void MarkNoticeShown()
    {
        try
        {
            VitalsPaths.EnsureDataDirectory();
            File.WriteAllText(NoticeShownFlag, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Worst case the notice is shown again next launch.
        }
    }

    /// <summary>
    /// Reads the firewall rules that apply to this executable on the currently active network
    /// profile. Requires no elevation. Returns <see cref="FirewallState.Unknown"/> on any failure,
    /// because a wrong "you are blocked" warning is worse than staying quiet.
    /// </summary>
    /// <param name="port">The TCP port the relay listens on, so port-only rules can be recognised.</param>
    public static FirewallVerdict Detect(int port = DefaultPort)
    {
        try
        {
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policyType is null)
            {
                return new FirewallVerdict(FirewallState.Unknown, "Unknown");
            }

            var policy = Activator.CreateInstance(policyType);
            if (policy is null)
            {
                return new FirewallVerdict(FirewallState.Unknown, "Unknown");
            }

            var activeProfiles = (int)Get(policy, "CurrentProfileTypes")!;
            var profileName = DescribeProfiles(activeProfiles);

            var rules = Get(policy, "Rules");
            if (rules is not System.Collections.IEnumerable enumerable)
            {
                return new FirewallVerdict(FirewallState.Unknown, profileName);
            }

            var exePath = TrayExecutablePath;
            var sawAllow = false;

            foreach (var rule in enumerable)
            {
                if (!RuleApplies(rule, exePath, port, activeProfiles, out var action))
                {
                    continue;
                }

                // A single Block wins outright: Windows evaluates Block rules before Allow rules,
                // so an Allow sitting alongside a Block does nothing at all.
                if (action != ActionAllow)
                {
                    return new FirewallVerdict(FirewallState.Blocked, profileName);
                }

                sawAllow = true;
            }

            return new FirewallVerdict(sawAllow ? FirewallState.Allowed : FirewallState.NoRule, profileName);
        }
        catch (Exception)
        {
            // The firewall COM surface is not contractual across Windows editions or locked-down
            // policies. Never let a detection failure take down the tray, and never guess: Unknown
            // means "say nothing", which is the right default for an advisory warning.
            return new FirewallVerdict(FirewallState.Unknown, "Unknown");
        }
    }

    /// <summary>
    /// Matches one COM rule against this relay: inbound, enabled, on an active profile, and
    /// either naming this executable or opening our TCP port for everything.
    ///
    /// Both forms have to count. A rule created by approving Windows' own prompt names the
    /// executable, but a rule added with <c>New-NetFirewallRule -LocalPort 5080</c> and no
    /// <c>-Program</c> names no application at all — and that is a perfectly ordinary way to open
    /// a port. Recognising only the first form makes the tray report "no rule" while traffic is
    /// in fact allowed, and then send the user to a fix that needs administrator rights for
    /// nothing.
    /// </summary>
    private static bool RuleApplies(object rule, string exePath, int port, int activeProfiles, out int action)
    {
        action = ActionAllow;

        try
        {
            var applicationName = Get(rule, "ApplicationName") as string;

            var matchesThisApp = !string.IsNullOrWhiteSpace(applicationName) &&
                string.Equals(applicationName, exePath, StringComparison.OrdinalIgnoreCase);

            // Only treat an application-less rule as ours if it actually covers our TCP port.
            // A rule for some other application tells us nothing about this one.
            var matchesOurPort = string.IsNullOrWhiteSpace(applicationName) &&
                (int)Get(rule, "Protocol")! == ProtocolTcp &&
                PortsInclude(Get(rule, "LocalPorts") as string, port);

            if (!matchesThisApp && !matchesOurPort)
            {
                return false;
            }

            if (Get(rule, "Enabled") is not bool { } enabled || !enabled)
            {
                return false;
            }

            if ((int)Get(rule, "Direction")! != DirectionInbound)
            {
                return false;
            }

            // Profiles is a bitmask; the rule is live only if it overlaps the active profile.
            if (((int)Get(rule, "Profiles")! & activeProfiles) == 0)
            {
                return false;
            }

            action = (int)Get(rule, "Action")!;
            return true;
        }
        catch (Exception)
        {
            // Individual rules can throw on malformed entries; skip them rather than abort the scan.
            return false;
        }
    }

    /// <summary>
    /// Whether a rule's LocalPorts covers <paramref name="port"/>. The COM value is free-form:
    /// "*" for any, a single port, a comma-separated list, or ranges such as "5000-6000".
    /// Anything unparseable is treated as not covering the port — a missed rule costs a needless
    /// warning, while a wrongly assumed one would tell the user they are reachable when they are
    /// not, and the display would just never update.
    /// </summary>
    private static bool PortsInclude(string? localPorts, int port)
    {
        if (string.IsNullOrWhiteSpace(localPorts))
        {
            return false;
        }

        if (localPorts.Trim() == "*")
        {
            return true;
        }

        foreach (var part in localPorts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (range.Length == 1 && int.TryParse(range[0], out var single) && single == port)
            {
                return true;
            }

            if (range.Length == 2 &&
                int.TryParse(range[0], out var from) &&
                int.TryParse(range[1], out var to) &&
                port >= from && port <= to)
            {
                return true;
            }
        }

        return false;
    }

    private static object? Get(object target, string property) =>
        target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);

    private static string DescribeProfiles(int profiles)
    {
        var names = new List<string>(3);
        if ((profiles & ProfileDomain) != 0) { names.Add("Domain"); }
        if ((profiles & ProfilePrivate) != 0) { names.Add("Private"); }
        if ((profiles & ProfilePublic) != 0) { names.Add("Public"); }
        return names.Count == 0 ? "Unknown" : string.Join(", ", names);
    }

    /// <summary>
    /// The remediation an administrator must run. Removing the Block rules first is not optional:
    /// Windows evaluates Block before Allow, so adding an Allow rule on its own changes nothing.
    /// </summary>
    public static string BuildRemediationScript(bool includePublic)
    {
        var profiles = includePublic ? "Private,Domain,Public" : "Private,Domain";
        var exePath = TrayExecutablePath;

        var script = new StringBuilder();
        script.AppendLine("# FidoRelay - allow the ESP32 display to reach the status endpoint.");
        script.AppendLine("# Run as Administrator.");
        script.AppendLine();
        script.AppendLine("# 1. Remove Block rules left behind by a dismissed firewall prompt.");
        script.AppendLine("#    Required first: Windows evaluates Block rules before Allow rules.");
        script.AppendLine("Get-NetFirewallRule -Direction Inbound -Action Block -ErrorAction SilentlyContinue |");
        script.AppendLine("  Where-Object { $_.DisplayName -like '*FidoRelay*' -or $_.DisplayName -like '*Claude Vitals*' } |");
        script.AppendLine("  Remove-NetFirewallRule -ErrorAction SilentlyContinue");
        script.AppendLine();
        script.AppendLine("# 2. Remove any previous copy of our own rule, so this is idempotent.");
        script.AppendLine("#    Includes the pre-rename name: that rule names the old install path as its");
        script.AppendLine("#    -Program, so it no longer matches anything and would just sit there forever.");
        script.AppendLine($"Get-NetFirewallRule -DisplayName '{RuleDisplayName}', '{LegacyRuleDisplayName}' -ErrorAction SilentlyContinue |");
        script.AppendLine("  Remove-NetFirewallRule -ErrorAction SilentlyContinue");
        script.AppendLine();
        script.AppendLine("# 3. Allow inbound TCP 5080 for the relay.");
        script.AppendLine($"New-NetFirewallRule -DisplayName '{RuleDisplayName}' -Direction Inbound `");
        script.AppendLine($"  -Program '{exePath}' `");
        script.AppendLine($"  -Protocol TCP -LocalPort 5080 -Profile {profiles} -Action Allow | Out-Null");
        script.AppendLine();
        script.AppendLine("Write-Host 'FidoRelay: firewall rule applied.' -ForegroundColor Green");

        if (!includePublic)
        {
            script.AppendLine();
            script.AppendLine("# Note: this does NOT allow the 'Public' profile. If this machine's network is");
            script.AppendLine("# marked Public, either mark it Private (recommended for a home/office LAN):");
            script.AppendLine("#   Set-NetConnectionProfile -InterfaceAlias 'Wi-Fi' -NetworkCategory Private");
            script.AppendLine("# or re-run including Public - but note the endpoint is unauthenticated.");
        }

        return script.ToString();
    }

    /// <summary>
    /// Writes the remediation script and runs it elevated. On a non-admin account this surfaces the
    /// UAC credential prompt, so an administrator standing by can approve it. Returns false if the
    /// user (or policy) declined, which is an ordinary outcome and not an error.
    /// </summary>
    public static bool TryApplyElevated(bool includePublic, out string message)
    {
        try
        {
            VitalsPaths.EnsureDataDirectory();
            var scriptPath = Path.Combine(VitalsPaths.DataDirectory, "fix-firewall.ps1");
            File.WriteAllText(scriptPath, BuildRemediationScript(includePublic));

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                Verb = "runas", // Triggers UAC; on a standard account this asks for admin credentials.
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                message = "Could not start the elevated helper.";
                return false;
            }

            process.WaitForExit(60_000);

            if (process.HasExited && process.ExitCode == 0)
            {
                message = "Firewall rule applied. Your display should be able to reach the relay now.";
                return true;
            }

            message = "The firewall command did not complete successfully.";
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 1223 = the user cancelled the UAC prompt, or has no administrator to hand.
            message =
                "Administrator rights are required to change firewall rules.\r\n\r\n" +
                "Use \"Copy firewall command\" instead and send it to whoever administers this machine.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = $"Could not write the firewall script: {ex.Message}";
            return false;
        }
    }

    /// <summary>Puts the remediation script on the clipboard, for a user who must ask IT to run it.</summary>
    public static bool TryCopyScriptToClipboard(bool includePublic)
    {
        try
        {
            Clipboard.SetText(BuildRemediationScript(includePublic));
            return true;
        }
        catch (Exception)
        {
            // The clipboard can be locked by another process; not worth surfacing as an error.
            return false;
        }
    }
}
