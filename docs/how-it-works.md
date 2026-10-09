# How it works

[← Back to the README](../README.md)


```
 Claude Code ──stdin JSON──▶ BorisCodeStatus.Hooks.exe ──writes──▶ %LOCALAPPDATA%\BorisCodeStatus\state.json
  (statusLine +                (runs once per event,                          │
   lifecycle hooks)             then exits)                                   │ FileSystemWatcher
                                                                              ▼
                                            BorisCodeStatus.Tray.exe ── hosts ──▶ GET /status  ◀── ESP32
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

### 2b. Session hooks — is a session open at all (`session_status`)

`activity` answers a narrower question than it looks like it does. `Stop` fires the moment a turn
finishes, so a session you are actively chatting in reports `Idle` for most of its wall-clock
life — all the time spent reading a reply and typing the next prompt. A display that wants
"a session is open" rather than "Claude is generating right now" needs a second signal.

| Hook | Verb argument | Effect |
|---|---|---|
| `SessionStart` | `sessionstart` | `session_status` → `Active`, clears any previous end |
| `SessionEnd` | `sessionend` | `session_status` → `Ended` |

These deliberately leave `activity` untouched — the two signals are independent.

`session_status` is **computed per request**, not stored, because a session going quiet writes
nothing to the state file and a stored value would sit at `Active` forever:

| Value | Meaning |
|---|---|
| `Unknown` | No session event seen yet in this install |
| `Active` | An event arrived within the last 15 minutes and no `SessionEnd` followed it |
| `Inactive` | No event for over 15 minutes — inferred, this is what a session killed without firing `SessionEnd` decays into |
| `Ended` | `SessionEnd` fired; the session closed cleanly |

Any session event refreshes it — statusLine and all four lifecycle hooks, not just the session
hooks — so `Active` holds through a normal conversation. The 15-minute timeout
(`VitalsState.SessionIdleTimeout`) is deliberately generous: flapping between `Active` and
`Inactive` while the user reads a long reply would be worse than reacting slowly.

### 3. `/api/oauth/usage` — opt-in fallback

An **undocumented** Anthropic endpoint, used only for the one figure hooks cannot supply: the
Sonnet-only weekly split (`week_sonnet`).

**It is off by default.** Tick **Settings → Advanced → Use usage API for Sonnet quota** to allow it. It is the
only part of the app that reads the OAuth token or talks to the network, for a figure most displays
do not show, from an endpoint that is not a contract — so it should be chosen, not assumed. The
preference is a marker file, `%LOCALAPPDATA%\BorisCodeStatus\usage-api-enabled.flag`, marking the
non-default *enabled* setting like every tray preference.

While it is off, `/status` serves `week_sonnet` and `usage_api_last_success_utc` as `null`, and
switching it off also clears any values already fetched from `state.json`. The poller and the
endpoint both read the preference each time, so toggling it needs no restart; after switching it
on, the first fetch happens at the next poll, within 10 minutes. It only ever runs while the HTTP
service is on, since `/status` is the only thing that uses the figure.

**Upgrading from 1.1 or earlier switches it off.** Earlier versions polled whenever the HTTP service
was running; tick the setting once to carry on.

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
| `BorisCodeStatus.Core` | `net10.0` | Models, state store, settings merger, usage API client |
| `BorisCodeStatus.Core.Tests` | `net10.0-windows` | Unit tests over parsing, state, merging, throttling, dog poses, starting/stopping the listener, preferences, firewall port matching |
| `BorisCodeStatus.Hooks` | `net10.0` | Console exe Claude Code invokes; self-contained single file |
| `BorisCodeStatus.Api` | `net10.0` | Minimal API **library** — the tray hosts it in-process |
| `BorisCodeStatus.Tray` | `net10.0-windows` | WinForms tray app; the only process that actually runs |
| `BorisCodeStatus.Installer` | WiX v4 | Produces `BorisCodeStatus.msi` |

`BorisCodeStatus.Api` is a library, not an executable: the tray app starts its `WebApplication`
in-process so there is one process to install, run and tray-manage. It stays a separate project so
the endpoint can be built and tested independently of the WinForms host.
