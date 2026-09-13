# Claude Code Vitals Relay

A small Windows user-mode background app that exposes Claude Code session and usage data over
HTTP on the local network, so an ESP32-based physical display (CrowPanel) can poll it.

It runs as a system-tray icon — no console window, no Windows service, no administrator rights.

```
 Claude Code ──stdin JSON──▶ ClaudeVitals.Hooks.exe ──writes──▶ %LOCALAPPDATA%\ClaudeVitals\state.json
  (statusLine +                (runs once per event,                          │
   lifecycle hooks)             then exits)                                   │ FileSystemWatcher
                                                                              ▼
                                            ClaudeVitals.Tray.exe ── hosts ──▶ GET /status  ◀── ESP32
                                             (tray icon + in-process API)         :5080
```

There is no IPC. The short-lived hook process and the long-lived tray process share one atomically
written JSON state file; the tray watches it for changes.

---

## Data sources

Three independent sources feed the app. They are not interchangeable.

### 1. statusLine hook — usage figures (primary, no network calls)

Claude Code pipes JSON to a configured command on session events. The fields used:

| Field | Becomes |
|---|---|
| `rate_limits.five_hour.used_percentage` / `.resets_at` | `session` |
| `rate_limits.seven_day.used_percentage` / `.resets_at` | `week` |
| `cost.total_cost_usd`, `cost.total_duration_ms` | `session_cost_usd`, `session_duration_ms` |
| `context_window.used_percentage` | `context_used_percentage` |
| `model.display_name`, `session_id`, `session_name` | `model_display_name`, `session_id`, `session_name` |

Fields absent from a payload keep their previous value rather than blanking the display —
`rate_limits` is omitted entirely on API-key sessions.

### 2. Lifecycle hooks — working / idle / waiting state

statusLine carries no lifecycle signal, so the tray animation state comes from separate hooks:

| Hook | Verb argument | State |
|---|---|---|
| `Notification` (permission needed) | `notification` | `Waiting` |
| `Stop` (turn finished) | `stop` | `Idle` |
| `PreToolUse` (tool about to run) | `pretooluse` | `Working` |
| `UserPromptSubmit` | `userpromptsubmit` | `Working` |

All four invoke the same executable; the verb argument distinguishes them.

### 3. `/api/oauth/usage` — fallback only

An **undocumented** Anthropic endpoint, used only for the one figure hooks cannot supply: the
Sonnet-only weekly split (`week_sonnet`).

It rate-limits aggressively and stays limited for an extended period once tripped, so:

- calls are floored at one per **5 minutes**, enforced across processes via a stamp file
- the background service polls no more often than every 10 minutes
- a `429` triggers an extra 30-minute back-off
- every failure is silent; the previous value simply stands

**Do not lower `UsageApiClient.MinimumInterval`.** A unit test guards it.

### No calendar-month figure

Claude Code exposes no month field anywhere, and no API endpoint provides one. `month_cost_usd` is
present in the response schema but is **always `null` in v1**. If a month figure is wanted later it
must be derived locally by summing JSONL transcript costs bucketed by month — not by inventing an
API call.

---

## Projects

| Project | Target | Role |
|---|---|---|
| `ClaudeVitals.Core` | `net10.0` | Models, state store, settings merger, usage API client |
| `ClaudeVitals.Core.Tests` | `net10.0` | 47 unit tests over parsing, state, merging, throttling |
| `ClaudeVitals.Hooks` | `net10.0` | Console exe Claude Code invokes; self-contained single file |
| `ClaudeVitals.Api` | `net10.0` | Minimal API **library** — the tray hosts it in-process |
| `ClaudeVitals.Tray` | `net10.0-windows` | WinForms tray app; the only process that actually runs |
| `ClaudeVitals.Installer` | WiX v4 | Produces `ClaudeVitals.msi` |

`ClaudeVitals.Api` is a library, not an executable: the tray app starts its `WebApplication`
in-process so there is one process to install, run and tray-manage. It stays a separate project so
the endpoint can be built and tested independently of the WinForms host.

---

## The `/status` endpoint

```
GET http://<host>:5080/status
GET http://<host>:5080/health
```

Bound to `0.0.0.0` so the ESP32 can reach it across the LAN. Port is overridable with the
`CLAUDEVITALS_PORT` environment variable. CORS allows all origins.

```json
{
  "session":  { "used_percentage": 42,    "resets_at": "2026-09-13T19:00:00+00:00", "resets_in_minutes": 656 },
  "week":     { "used_percentage": 61.25, "resets_at": "2026-09-17T04:30:00+00:00", "resets_in_minutes": 5546 },
  "week_sonnet": null,
  "context_used_percentage": 37.5,
  "model_display_name": "Opus 5",
  "session_id": "abc-123",
  "session_name": "vitals relay",
  "session_cost_usd": 1.2345,
  "session_duration_ms": 843000,
  "month_cost_usd": null,
  "activity": "Working",
  "activity_changed_utc": "2026-09-13T08:03:43+00:00",
  "last_updated_utc": "2026-09-13T08:03:43+00:00",
  "usage_api_last_success_utc": null,
  "age_seconds": 0
}
```

`resets_in_minutes` and `age_seconds` are computed per request, so the firmware does not need a
clock or timezone handling. `age_seconds` lets the display grey out stale data.

### ⚠️ Security: the endpoint is unauthenticated

`/status` exposes session cost, usage percentages and session names to **anything on the LAN**, with
no authentication. This is a deliberate v1 decision for a home or small-office network — the ESP32
is not a browser and has nowhere to keep a credential.

Do not run this on an untrusted or shared network (a co-working space, a hotel, a guest VLAN)
without adding auth first. If you need it, the natural v1.1 is a static bearer token in a header,
checked in a one-line middleware, with the token stored next to `state.json`.

---

## Install

```
ClaudeVitals.msi
```

**No administrator rights are required**, by design:

- per-user MSI (`Scope="perUser"`, no `ALLUSERS`) — no UAC prompt
- installs to `%LOCALAPPDATA%\Programs\ClaudeVitals` — not `Program Files`
- starts at login via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- no Windows service, no scheduled task
- Kestrel binds `0.0.0.0:5080` with a plain socket, so no `netsh http add urlacl` reservation is
  needed (that is only required for http.sys / `HttpListener`)

Both executables ship self-contained, so the target machine needs no .NET runtime installed. That
is why the MSI is ~75 MB.

### The one place admin can appear: Windows Firewall

Installing needs no admin rights. **Reaching the endpoint from the ESP32 usually does**, and this
is the step most likely to catch you out. It has been hit in practice on a real install.

The first time Kestrel binds a non-loopback address, Windows shows a firewall prompt. Approving it
requires administrator rights. If a non-admin user dismisses or cancels it — which is all they can
do — Windows does not simply skip the rule: it **creates `Block` rules** for that executable.
`/status` then still works from `localhost`, but the ESP32 is refused.

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
Get-NetFirewallRule -Direction Inbound | Where-Object DisplayName -like "*Vitals*" |
  Select-Object DisplayName, Action, Profile
```

The rules that apply are the ones matching the **active** `NetworkCategory`. A laptop on Wi-Fi is
frequently `Public`, not `Private`.

**The app handles most of this for you.** Before it binds the port on first run it shows a dialog
explaining that the Windows prompt is about to appear and why "Allow access" matters. On every
start it checks the firewall (reading rules needs no elevation) and warns with a tray balloon if
the display cannot reach it. The tray menu item **Fix firewall access...** reports the current state
and offers either to run the repair elevated — surfacing the UAC prompt so an administrator can
approve it — or to copy the exact command to send to whoever administers the machine. It refuses to
open the `Public` profile unless you explicitly confirm.

The manual equivalent, for an administrator fixing it once per machine:

```powershell
# 1. Remove any Block rules left behind by a dismissed prompt
Get-NetFirewallRule -Direction Inbound -Action Block |
  Where-Object { $_.DisplayName -like "*ClaudeVitals*" -or $_.DisplayName -like "claudevitals*" } |
  Remove-NetFirewallRule

# 2. Mark the network Private, if it is genuinely a home or office LAN
Set-NetConnectionProfile -InterfaceAlias "Wi-Fi" -NetworkCategory Private

# 3. Allow the relay on that profile only
New-NetFirewallRule -DisplayName "Claude Vitals Relay" -Direction Inbound `
  -Program "$env:LOCALAPPDATA\Programs\ClaudeVitals\ClaudeVitals.Tray.exe" `
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
  surfaceable through the tray's **Re-register hooks** menu item.

The MSI launches the tray app once at the end of a successful install, so registration happens
during installation from the user's point of view.

**`settings.json` is read-modify-written as a JSON tree, never regenerated.** Unrelated
configuration (permissions, theme, env, other hooks) is preserved, the original is backed up once
to `settings.json.claudevitals.bak`, and an unparseable file is left completely untouched.

**An existing third-party `statusLine` is never overwritten.** If one is present the app leaves it
alone and warns instead — remove the `statusLine` entry from `settings.json` by hand to switch over.
Lifecycle hooks are appended alongside any existing hooks, not replaced.

Registered entries:

```json
{
  "statusLine": {
    "type": "command",
    "command": "\"%LOCALAPPDATA%\\Programs\\ClaudeVitals\\ClaudeVitals.Hooks.exe\" statusline"
  },
  "hooks": {
    "Notification":     [{ "hooks": [{ "type": "command", "command": "\"...\" notification" }] }],
    "Stop":             [{ "hooks": [{ "type": "command", "command": "\"...\" stop" }] }],
    "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": "\"...\" userpromptsubmit" }] }],
    "PreToolUse":       [{ "matcher": "*", "hooks": [{ "type": "command", "command": "\"...\" pretooluse" }] }]
  }
}
```

Claude Code picked the hooks up mid-session on the install that was tested here, without a restart.
That is not documented behaviour, so if `/status` still reports `null` a minute after installing,
restart Claude Code before investigating further.

---

## Tray icon

Drawn at runtime rather than shipped as `.ico` assets, so it encodes live data:

- **fill colour** — grey `Unknown`, blue `Idle`, green `Working`, amber `Waiting`
- **surrounding arc** — five-hour session quota used, turning red past 80%

Right-click menu: current session and week figures (display-only), **Open dashboard** (opens
`/status` in the browser), **Re-register hooks**, **Exit**. Double-click opens the dashboard.

---

## Build

Requires the .NET 10 SDK and the WiX v4 CLI:

```bash
dotnet tool install --global wix --version 4.0.5
dotnet build -c Release
```

A Release build of the solution produces `ClaudeVitals.Installer\bin\Release\ClaudeVitals.msi` as a
normal build output — no separate packaging step.

> **WiX version:** pinned to **4.0.5**. WiX v7 requires accepting the Open Source Maintenance Fee
> EULA, which is a commercial licensing decision rather than a technical one. v4 has no such
> requirement. Revisit only if someone signs off on the OSMF terms.

Run the tests:

```bash
dotnet test ClaudeVitals.Core.Tests
```

### Releases (GitHub Actions)

`.github/workflows/build.yml` builds and tests on every push and pull request, and publishes a
GitHub Release when a `v*` tag is pushed:

```bash
git tag v1.2.3
git push origin v1.2.3
```

The tag sets the MSI's `ProductVersion`, so **it must increase between releases** — `MajorUpgrade`
will refuse to replace an installed copy otherwise. The workflow rejects a tag that is not plain
`major.minor.build`: an MSI `ProductVersion` cannot carry a `-rc1` style suffix, and failing early
with a clear message beats failing deep inside the WiX build.

Before publishing, the workflow checks the built MSI: the version was stamped, both executables are
in the payload, and `ALLUSERS` is unset — that last one is a regression guard, because setting it
would quietly turn this back into a package that demands administrator rights.

Every run uploads the MSI as a build artifact, tag or not.

### Artifact cleanup

Each build artifact is around 78 MB, so they dominate the repository's storage quota within days.
`.github/workflows/cleanup-artifacts.yml` prunes them **daily** at 17:00 UTC — daily rather than
weekly purely because of that size, since the job itself is only a few API calls.

It deletes artifacts older than 7 days but always keeps the 3 most recent whatever their age, so
there is always something downloadable. It only ever touches workflow *artifacts*: release assets
are a separate API and are never considered, so a published release cannot be affected.

Run it by hand from the Actions tab to override the retention, or with **dry run** ticked to see
what would go without deleting anything.

Worth setting alongside it: GitHub's repository-level artifact retention (Settings → Actions →
General) defaults to 90 days. Lowering it to 7–14 days gives the same result without any workflow
running, and the two are complementary — the workflow additionally guarantees the most recent few
survive.

To build a versioned MSI locally:

```bash
dotnet build -c Release -p:Version=1.2.3
```

### Testing the hook by hand

```bash
echo '{"model":{"display_name":"Opus 5"},"rate_limits":{"five_hour":{"used_percentage":42}}}' \
  | ClaudeVitals.Hooks.exe statusline

echo '{"hook_event_name":"Notification"}' | ClaudeVitals.Hooks.exe notification
```

Then check `%LOCALAPPDATA%\ClaudeVitals\state.json`, or `curl http://localhost:5080/status` with the
tray running.

---

## Design notes

**The hook executable must be fast and must never fail.** Claude Code cancels an in-flight
statusLine script when the next event arrives, and a slow script stalls status line updates. So the
hook process does file I/O only — never network — finishes in roughly 200 ms, runs under a 3-second
watchdog, and **always exits 0**, even on malformed input or an unrecognised verb. A broken status
line is a far worse outcome than a missed update.

**It also occupies the user's statusLine slot**, so it prints a compact useful line
(`Opus 5  5h 42%  7d 61%  ctx 38%`) rather than nothing.

**State writes are atomic and cross-process safe**: serialised by a named mutex, written to a temp
file and moved into place, so a reader sees either the old file or the new one, never a partial one.
Reads share every file mode and swallow transient I/O errors.

---

## Known limitations

- **Uninstall does not remove the hook entries from `~/.claude/settings.json`.** The Run key,
  installed files and Start Menu shortcut are all removed cleanly, but the `statusLine` and `hooks`
  entries remain and will point at a missing executable. Claude Code tolerates this (the commands
  simply fail), but the entries should be removed by hand, or restored from
  `settings.json.claudevitals.bak`. Automating this is a v1.1 item.
- **`month_cost_usd` is always `null`.** See above — no data source provides it.
- **`week_sonnet` depends on an undocumented endpoint** whose response shape is not contractual. The
  client tries several plausible property spellings and returns `null` rather than guessing wrong.
  It may simply never populate.
- **No authentication on `/status`.** See the security note above.
- **Running a development build can hijack your real `~/.claude/settings.json`.** The tray registers
  whatever path it is currently running from. If `ClaudeVitals.Hooks.exe` happens to sit next to the
  tray exe in `bin\Debug\...` or `bin\Release\...`, that throwaway build path is written into your
  global settings — and once the directory is cleaned, every hook silently fails forever, because
  the hook process is deliberately built never to report errors. The symptom is `/status` returning
  `null` for everything with nothing logged anywhere. **This has happened in practice.** Check with:
  ```powershell
  Select-String -Path "$env:USERPROFILE\.claude\settings.json" -Pattern "ClaudeVitals.Hooks.exe"
  ```
  If the path points inside a `bin\` folder, reinstall the MSI or use **Re-register hooks** from the
  tray menu to repoint it. A fix — refusing to register from a `bin\`/`obj\` path, and warning when
  the registered path no longer exists — is a v1.1 item.
- **The MSI has been installed and verified on a developer machine, not on a clean VM.** Confirmed
  on a real per-user install: `msiexec` exit code 0 with no UAC prompt, product registered and
  uninstallable, files in `%LOCALAPPDATA%\Programs\ClaudeVitals`, HKCU Run key set, hooks
  re-registered to the install path automatically, and live session data served from `/status`.
  What that install did *not* cover: a machine with no prior ClaudeVitals state, and the upgrade and
  uninstall paths. Those are still worth exercising on a fresh VM before distributing.
