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

The first time Kestrel binds a non-loopback address, Windows may show a firewall prompt that only
an administrator can approve. If it is dismissed or blocked, `/status` still works from
`localhost` but **not from the ESP32**.

An administrator can pre-create the rule once, per machine:

```powershell
New-NetFirewallRule -DisplayName "Claude Vitals Relay" -Direction Inbound `
  -Program "$env:LOCALAPPDATA\Programs\ClaudeVitals\ClaudeVitals.Tray.exe" `
  -Protocol TCP -LocalPort 5080 -Profile Private -Action Allow
```

Restrict to `-Profile Private` so the endpoint is never exposed on a public network.

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

Restart Claude Code after installing for the hooks to take effect.

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
- **The MSI was verified by static inspection and payload extraction, not by a full install on a
  clean VM.** Package scope, the HKCU Run key, the install directory chain, and both executables
  running from the extracted layout were all confirmed; an end-to-end install on a fresh machine is
  still the right final check before distributing.
