# Install

[← Back to the README](../README.md)


```
BorisCodeStatus.msi
```

**No administrator rights are required**, by design:

- per-user MSI (`Scope="perUser"`, no `ALLUSERS`) — no UAC prompt
- installs to `%LOCALAPPDATA%\Programs\BorisCodeStatus` — not `Program Files`
- starts at login via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- no Windows service, no scheduled task
- once the HTTP service is switched on, Kestrel binds `0.0.0.0:5080` with a plain socket, so no
  `netsh http add urlacl` reservation is needed (that is only required for http.sys / `HttpListener`)

Both executables ship self-contained, so the target machine needs no .NET runtime installed. That
is why the MSI is ~75 MB.

### The one place admin can appear: Windows Firewall

Installing needs no admin rights. **Reaching the endpoint from the ESP32 usually does**, and this
is the step most likely to catch you out. It has been hit in practice on a real install.

The first time Kestrel binds a non-loopback address — that is, the first time you enable the HTTP
service — Windows shows a firewall prompt. Approving it
requires administrator rights. If a non-admin user dismisses or cancels it — which is all they can
do — Windows does not simply skip the rule: it **creates `Block` rules** for that executable.
`/status` then still works from `localhost`, but the ESP32 is refused.

**Upgrading does not cost you this again.** Firewall rules key on the executable's *path*, not its
contents, so replacing the binary in place leaves them matching; `ShowFirstRunNoticeIfNeeded` and
the warning balloon both stop early once `Detect()` returns `Allowed`. A prompt only reappears if
the path itself changes.

`Detect()` recognises **both shapes of rule**: one naming this executable, as approving Windows'
own prompt creates, and one naming no application but opening our TCP port, as
`New-NetFirewallRule -LocalPort 5080` creates. Both genuinely allow the traffic, and treating the
second as "no rule" would send the user to a fix needing administrator rights for nothing. The
port is matched against every shape the COM API reports — `*`, a single port, a comma-separated
list, and ranges — and anything unparseable counts as *not* covering the port, so a missed rule
costs a needless warning rather than a false assurance of reachability.

Two things make this harder to diagnose than it looks:

- **Block beats Allow.** Windows Firewall evaluates `Block` rules before `Allow` rules, so adding
  an Allow rule on top of the auto-created Block rules does nothing. The Block rules must be
  removed first.
- **Testing from the machine itself proves nothing.** A request to the host's own LAN address
  (`http://192.168.x.x:5080/health` from that same machine) bypasses inbound firewall filtering and
  returns `200` even when every external device is blocked. **Only a request from another device is
  a real test.**

Check the actual state before trusting it:

```powershell
Get-NetConnectionProfile | Select-Object InterfaceAlias, NetworkCategory
Get-NetFirewallRule -Direction Inbound | Where-Object DisplayName -like "*BorisCodeStatus*" |
  Select-Object DisplayName, Action, Profile
```

The rules that apply are the ones matching the **active** `NetworkCategory`. A laptop on Wi-Fi is
frequently `Public`, not `Private`.

**None of this applies while the HTTP service is off**, which is the default. Nothing binds the
port, so Windows never prompts, the app never checks or warns, and **Fix firewall access...** is
greyed out — a rule for a port nothing is listening on would open the firewall for no reason.

**Once it is on, the app handles most of this for you.** Before it first binds the port it shows a
dialog explaining that the Windows prompt is about to appear and why "Allow access" matters. On
every start with the service on, and whenever you switch it on, it checks the firewall (reading
rules needs no elevation) and warns with a tray balloon if the display cannot reach it. The tray
menu item **Fix firewall access...** reports the current state
and offers either to run the repair elevated — surfacing the UAC prompt so an administrator can
approve it — or to copy the exact command to send to whoever administers the machine. It refuses to
open the `Public` profile unless you explicitly confirm.

The manual equivalent, for an administrator fixing it once per machine:

```powershell
# 1. Remove any Block rules left behind by a dismissed prompt
Get-NetFirewallRule -Direction Inbound -Action Block |
  Where-Object { $_.DisplayName -like "*BorisCodeStatus*" -or $_.DisplayName -like "boriscodestatus*" } |
  Remove-NetFirewallRule

# 2. Mark the network Private, if it is genuinely a home or office LAN
Set-NetConnectionProfile -InterfaceAlias "Wi-Fi" -NetworkCategory Private

# 3. Allow the relay on that profile only
New-NetFirewallRule -DisplayName "BorisCodeStatus" -Direction Inbound `
  -Program "$env:LOCALAPPDATA\Programs\BorisCodeStatus\BorisCodeStatus.Tray.exe" `
  -Protocol TCP -LocalPort 5080 -Profile Private -Action Allow
```

Prefer `-Profile Private` over `Private,Public`. `/status` is unauthenticated, so opening it on a
network Windows considers public exposes session cost and usage data to strangers. If the only way
to reach the display is to allow `Public`, add authentication first.

### Hook registration: first-run logic, not an MSI custom action

The app registers itself in `~/.claude/settings.json` **from the tray app's startup path**, not from
an installer custom action. The choice is deliberate:

- A per-user MSI custom action runs in a context where `%USERPROFILE%` is not reliably the
  installing user's — especially under deployment tooling. First-run logic always has the right user.
- The merge is idempotent, so running it on every launch also repairs a stale path after an upgrade.
  A custom action only ever runs at install time.
- A failure in a custom action fails the install. A failure at startup is recoverable and
  surfaceable through the **Re-register hooks** button under **Settings → Advanced**.

The MSI launches the tray app once at the end of a successful install, so registration happens
during installation from the user's point of view.

**`settings.json` is read-modify-written as a JSON tree, never regenerated.** Unrelated
configuration (permissions, theme, env, other hooks) is preserved, the original is backed up once
to `settings.json.boriscodestatus.bak`, and an unparseable file is left completely untouched.

**An existing third-party `statusLine` is never overwritten.** If one is present the app leaves it
alone and warns instead — remove the `statusLine` entry from `settings.json` by hand to switch over.
Lifecycle hooks are appended alongside any existing hooks, not replaced.

Registered entries:

```json
{
  "statusLine": {
    "type": "command",
    "command": "\"%LOCALAPPDATA%\\Programs\\BorisCodeStatus\\BorisCodeStatus.Hooks.exe\" statusline"
  },
  "hooks": {
    "Notification":     [{ "hooks": [{ "type": "command", "command": "\"...\" notification" }] }],
    "Stop":             [{ "hooks": [{ "type": "command", "command": "\"...\" stop" }] }],
    "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": "\"...\" userpromptsubmit" }] }],
    "PreToolUse":       [{ "matcher": "*", "hooks": [{ "type": "command", "command": "\"...\" pretooluse" }] }],
    "SessionStart":     [{ "hooks": [{ "type": "command", "command": "\"...\" sessionstart" }] }],
    "SessionEnd":       [{ "hooks": [{ "type": "command", "command": "\"...\" sessionend" }] }]
  }
}
```

Claude Code picked the hooks up mid-session on the install that was tested here, without a restart.
That is not documented behaviour, so if `/status` still reports `null` a minute after installing,
restart Claude Code before investigating further.
