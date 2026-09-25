# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows user-mode tray app that relays Claude Code session/usage data over HTTP on the LAN so an
ESP32 display can poll it. No service, no admin rights, no IPC. `README.md` is unusually thorough —
read it before designing anything; it documents the security posture, the release process and the
known limitations, and it is expected to stay current with each change.

## Commands

```bash
dotnet build -c Release                       # also produces BorisClaudeNotifications.Installer\bin\Release\BorisClaudeNotifications.msi
dotnet build -c Release -p:Version=1.2.3      # versioned MSI
dotnet test BorisClaudeNotifications.Core.Tests
dotnet test BorisClaudeNotifications.Core.Tests --filter "FullyQualifiedName~SessionStatusTests"
dotnet test BorisClaudeNotifications.Core.Tests --filter "DisplayName~IdleTurnIsStillAnActiveSession"
```

Requires the .NET 10 SDK and the WiX v4 CLI (`dotnet tool install --global wix --version 4.0.5`).
WiX is pinned to 4.0.5 deliberately — v7 requires accepting the Open Source Maintenance Fee EULA,
which is a licensing decision, not a technical one.

Exercising a hook by hand (see README for more verbs):

```bash
echo '{"hook_event_name":"Notification"}' | BorisClaudeNotifications.Hooks.exe notification
curl http://localhost:5080/status
```

Releases are published by manually running the **Release** workflow from the Actions tab, never by
pushing a tag. `build.yml` runs on every push/PR to `main`.

**`<Version>` in `Directory.Build.props` is the single source of truth for the version** — it is
stamped on every assembly, shown in the tray menu, and read by the release workflow to pick the tag
and the MSI `ProductVersion`. It is deliberately not a workflow input, so a release cannot claim a
version the installed app disagrees with. To release: raise it, merge, then run the workflow.

## Architecture

Two processes, no IPC between them — they share one JSON file:

```
Claude Code ──stdin JSON──▶ BorisClaudeNotifications.Hooks.exe ──writes──▶ %LOCALAPPDATA%\BorisClaudeNotifications\state.json
                            (runs once per event, exits)                    │ FileSystemWatcher
                                          BorisClaudeNotifications.Tray.exe ──hosts──▶ GET /status :5080 ◀── ESP32
```

- **Core** — models, `VitalsStateStore` (the shared file, mutex-serialised and atomically written),
  `VitalsUpdates` (pure payload→state mapping, kept filesystem-free so it is unit testable), and
  `ClaudeSettingsMerger`.
- **Hooks** — one `Exe` for every hook; the verb argument (`statusline`, `stop`, `sessionstart`, …)
  selects the behaviour. Registered in `ClaudeSettingsMerger.LifecycleHooks`.
- **Api** — a *library*, not an executable. `VitalsApi` builds the `WebApplication`;
  `VitalsApiHost` owns its lifetime so the tray can pause and resume the listener. A stopped
  `WebApplication` cannot be restarted, so resuming builds a new one — that is why the host holds
  the options and store rather than the app.
- **Tray** — WinForms host: owns the message loop, starts the API, draws the icon from live data,
  and registers hooks on every launch (idempotent, so it repairs a stale path after an upgrade).

The tray glyph is an 8-bit dog in a quota ring, drawn at runtime from palette-index grids in
`DogSprites.cs` (one char per pixel). Blit 1:1 only — scaling destroys it — and do not grow the
sprite past 20px on the 32px canvas or the ring clips it. The pose rule is `DogStates.For` in
**Core**, not the tray, because the ESP32 must derive the same pose from the same state.

`VitalsState` **is** the `/status` contract — snake_case `JsonPropertyName` on every member, keyed
off directly by ESP32 firmware. Adding a field changes the wire format; update the README payload
sample in the same change.

## Conventions that carry real weight here

**The hook process must be fast and must never fail.** Claude Code cancels an in-flight statusLine
script when the next event arrives. So: file I/O only, never network; a 3-second watchdog; and it
**always exits 0**, even on malformed input or an unknown verb. Never add a throwing path or a
network call to `BorisClaudeNotifications.Hooks`.

**Derived values are computed on read, not stored.** `age_seconds`, `resets_in_minutes` and
`session_status` are get-only properties on the serialised record. The reason is structural: nothing
writes to `state.json` while a session is quiet, so a stored value would never decay. Follow the
same pattern for anything time-dependent.

**`activity` and `session_status` answer different questions.** `activity` is what Claude is doing
this turn (`Stop` sets `Idle` the instant a turn ends, so a live conversation reads `Idle` most of
the time); `session_status` is whether a session is open at all. Keep them independent — the session
hooks deliberately leave `activity` untouched.

**Tray preferences are marker files, not `state.json` fields** (`FlagPreference`, in `Core/State`).
`state.json` is the wire payload the hook process rewrites constantly; a preference does not belong
in it or on `/status`. The file always marks the **non-default** setting, so a missing or unreadable
preference yields the default and there is no first-run write — hence `http-paused.flag` (default
running) and `notifications-disabled.flag` (default on).

**`Refresh` runs on every state write *and* every pose-timer tick.** Anything with a side effect —
a notification, a balloon — must fire on a transition, keyed off `activity_changed_utc`, not on
"state is currently X", or it repeats forever.

**Never regenerate `~/.claude/settings.json`.** It holds unrelated user config. The merger
read-modify-writes a JSON tree, backs the file up once, leaves a third-party `statusLine` alone with
a warning rather than clobbering it, and refuses to touch a file it cannot parse.

**Comments explain why, not what,** and openly record what is unverified — the `/api/oauth/usage`
fallback is undocumented and best-effort, `month_cost_usd` has no data source and is always null.
Match that register; the existing XML doc comments are the house style.

## Traps

- **Running a development build hijacks your real `~/.claude/settings.json`.** The tray registers
  whatever path it is running from, so a `bin\Debug\...` path can end up in your global settings —
  and once cleaned, every hook fails silently forever, because the hook is built never to report
  errors. Check with `Select-String -Path "$env:USERPROFILE\.claude\settings.json" -Pattern "BorisClaudeNotifications.Hooks.exe"`
  and repair with the tray's **Re-register hooks**.
- `/status` is unauthenticated and bound to `0.0.0.0` by design in v1. Do not quietly widen what it
  exposes; it already serves session names and cost to anything on the LAN.
- `VitalsStateStore.JsonOptions` is `internal` and `BorisClaudeNotifications.Core` grants no `InternalsVisibleTo` —
  construct local `JsonSerializerOptions(JsonSerializerDefaults.Web)` in tests. (`BorisClaudeNotifications.Tray`
  *does* grant it, so its `internal` members are testable.)
- **Anything time-dependent must take its instant as a parameter, and the whole decision must use
  that one instant.** `DogStates.For(state, now)` mixed an injected clock with `UtcNow` inside
  `SessionStatus`; it agreed at runtime, where both are "now", and made the tests pass one day and
  fail the next. Hence `VitalsState.SessionStatusAt(now)`.
- The MSI must never set `ALLUSERS`; the release workflow asserts this, because setting it would
  turn a per-user install back into one demanding administrator rights.
