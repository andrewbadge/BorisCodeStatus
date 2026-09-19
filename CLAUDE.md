# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows user-mode tray app that relays Claude Code session/usage data over HTTP on the LAN so an
ESP32 display can poll it. No service, no admin rights, no IPC. `README.md` is unusually thorough —
read it before designing anything; it documents the security posture, the release process and the
known limitations, and it is expected to stay current with each change.

## Commands

```bash
dotnet build -c Release                       # also produces ClaudeVitals.Installer\bin\Release\ClaudeVitals.msi
dotnet build -c Release -p:Version=1.2.3      # versioned MSI
dotnet test ClaudeVitals.Core.Tests
dotnet test ClaudeVitals.Core.Tests --filter "FullyQualifiedName~SessionStatusTests"
dotnet test ClaudeVitals.Core.Tests --filter "DisplayName~IdleTurnIsStillAnActiveSession"
```

Requires the .NET 10 SDK and the WiX v4 CLI (`dotnet tool install --global wix --version 4.0.5`).
WiX is pinned to 4.0.5 deliberately — v7 requires accepting the Open Source Maintenance Fee EULA,
which is a licensing decision, not a technical one.

Exercising a hook by hand (see README for more verbs):

```bash
echo '{"hook_event_name":"Notification"}' | ClaudeVitals.Hooks.exe notification
curl http://localhost:5080/status
```

Releases are published by manually running the **Release** workflow from the Actions tab, never by
pushing a tag. `build.yml` runs on every push/PR to `main`.

## Architecture

Two processes, no IPC between them — they share one JSON file:

```
Claude Code ──stdin JSON──▶ ClaudeVitals.Hooks.exe ──writes──▶ %LOCALAPPDATA%\ClaudeVitals\state.json
                            (runs once per event, exits)                    │ FileSystemWatcher
                                          ClaudeVitals.Tray.exe ──hosts──▶ GET /status :5080 ◀── ESP32
```

- **Core** — models, `VitalsStateStore` (the shared file, mutex-serialised and atomically written),
  `VitalsUpdates` (pure payload→state mapping, kept filesystem-free so it is unit testable), and
  `ClaudeSettingsMerger`.
- **Hooks** — one `Exe` for every hook; the verb argument (`statusline`, `stop`, `sessionstart`, …)
  selects the behaviour. Registered in `ClaudeSettingsMerger.LifecycleHooks`.
- **Api** — a *library*, not an executable. Builds the `WebApplication`; the tray hosts it
  in-process so there is only one process to install and supervise.
- **Tray** — WinForms host: owns the message loop, starts the API, draws the icon from live data,
  and registers hooks on every launch (idempotent, so it repairs a stale path after an upgrade).

`VitalsState` **is** the `/status` contract — snake_case `JsonPropertyName` on every member, keyed
off directly by ESP32 firmware. Adding a field changes the wire format; update the README payload
sample in the same change.

## Conventions that carry real weight here

**The hook process must be fast and must never fail.** Claude Code cancels an in-flight statusLine
script when the next event arrives. So: file I/O only, never network; a 3-second watchdog; and it
**always exits 0**, even on malformed input or an unknown verb. Never add a throwing path or a
network call to `ClaudeVitals.Hooks`.

**Derived values are computed on read, not stored.** `age_seconds`, `resets_in_minutes` and
`session_status` are get-only properties on the serialised record. The reason is structural: nothing
writes to `state.json` while a session is quiet, so a stored value would never decay. Follow the
same pattern for anything time-dependent.

**`activity` and `session_status` answer different questions.** `activity` is what Claude is doing
this turn (`Stop` sets `Idle` the instant a turn ends, so a live conversation reads `Idle` most of
the time); `session_status` is whether a session is open at all. Keep them independent — the session
hooks deliberately leave `activity` untouched.

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
  errors. Check with `Select-String -Path "$env:USERPROFILE\.claude\settings.json" -Pattern "ClaudeVitals.Hooks.exe"`
  and repair with the tray's **Re-register hooks**.
- `/status` is unauthenticated and bound to `0.0.0.0` by design in v1. Do not quietly widen what it
  exposes; it already serves session names and cost to anything on the LAN.
- `VitalsStateStore.JsonOptions` is `internal`, and the test project has no `InternalsVisibleTo` —
  construct local `JsonSerializerOptions(JsonSerializerDefaults.Web)` in tests.
- The MSI must never set `ALLUSERS`; the release workflow asserts this, because setting it would
  turn a per-user install back into one demanding administrator rights.
