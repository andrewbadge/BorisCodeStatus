# OpenCode as a second source: plan

[← Back to the README](../README.md)

**Status: proposed, not started.** This page is the implementation plan for adding
[OpenCode](https://opencode.ai/) as an alternative to Claude Code in both builds. It is written so
the work can be picked up later without re-deriving the API, the mapping or the decisions. As parts
ship they move into the regular docs (`privacy.md`, `how-it-works.md`, `SPECS.md`,
`known-limitations.md`) and this page shrinks to the reasoning.

The rule the whole plan follows: **OpenCode is a second producer for the same `state.json`, not a
replacement for Claude Code.** Nothing downstream of the state file changes. Every existing Claude
path stays; the source is a preference, and Claude remains the default.

---

## Goal and non-goals

**Goal.** With **Settings → Source → OpenCode**, the pet, cards, sounds, warning card and (on
Windows) `GET /status` show what an OpenCode session is doing — working, waiting for a permission,
or idle — and, when OpenCode Go is in use, its rolling and weekly meters. With **Source → Claude**
the app behaves exactly as it does today.

**Not in scope.**

- No Node/TypeScript plugin, adapter or sidecar. Both clients are native: C# in
  `windows/BorisCodeStatus.Core`, Swift in `macos/Sources/BorisCodeStatusCore`.
- No attempt to start, stop or install the OpenCode service. If it is not running, the app says so
  and retries; it never spawns another app's daemon.
- No Anthropic subscription quota through OpenCode. See [Quota](#quota).
- No new fields in `VitalsState` or `/status`. The wire contract is unchanged.
- No simultaneous sources. One is active at a time; switching writes a clean state.

---

## What is already true

Verified against OpenCode **2.2.5** on this Mac:

| Thing | Finding |
|---|---|
| Service | `opencode serve --service` listening on a loopback port (`http://127.0.0.1:49374` here) |
| Registration file | `~/.local/state/opencode/service.json`, mode `0600` |
| Contents | `id`, `version`, `url`, `pid`, `password` — the password is the service secret |
| Auth | HTTP Basic, username literally `opencode`; unauthenticated requests return `401` |
| Event stream | `GET /api/event` → `text/event-stream`, `data: {json}` lines, `: heartbeat` comments |
| Replay | None advertised. Treat the stream as live-only and resync on every connect |
| Go meters | `GET /api/experimental/usage/limits` → `go.rolling/weekly/monthly` (`percent`, `limited`, `resetsAt` in ms) |

The API document shipped with 2.2.5 describes the endpoints and event payloads used below; the
public reference is <https://opencode.ai/v2/docs/api>. Its own description calls the surface
**experimental**, which is why the client must be defensive about unknown events and must pin a
tested version in the docs.

Two things are **not** verified yet and are Phase 0 work:

1. The Windows registration-file location. OpenCode resolves its state directory through XDG-style
   paths; on Windows that has been reported both as `%USERPROFILE%\.local\state\opencode` and as
   `%LOCALAPPDATA%\opencode`. The client will probe both (and `XDG_STATE_HOME`) rather than assume.
2. That `session.status` is emitted live. It is advertised in the `/api/event?types=` filter and
   has a `SessionStatusUpdated` schema, but it is absent from the `V2Event` union in 2.2.5. The
   mapper therefore treats `session.idle` and the execution events as the primary signals and
   `session.status` as an optimisation.

The registration file name is not always `service.json`: a non-default channel writes
`service-<channel>.json` (and older versions wrote a password-only `~/.config/opencode/service.json`).
Discovery must glob `service*.json` in the state directory, prefer the running `pid`, and ignore the
config-directory file once a state-directory file is usable.

---

## Design

### The seam

```
Claude Code ──stdin JSON──▶ Hooks ──writes──┐
                                            ├──▶ state.json ──▶ tray / menu bar / cards / /status
OpenCode ──HTTP + SSE──▶ OpenCodeSource ────┘        │
   (loopback)             (tray / menu bar)          └──▶ FileSystemWatcher / DirectoryWatcher
```

There is still one long-lived process per platform and one shared file. The OpenCode client lives in
the tray process on Windows and the menu-bar app on macOS, because it needs to be long-lived; the
hook process stays short-lived, file-only and network-free.

No `IVitalsSource` abstraction is introduced yet. Claude is a **push** model (hooks) and OpenCode is
a **pull/stream** model (HTTP + SSE); the only thing they share is the state file, and an interface
would add a lifecycle layer without removing a branch. If a third streaming source ever appears,
introduce the abstraction then.

### Discovery and authentication

`OpenCodeDiscovery` in both languages:

1. Build the candidate state directories, in order:
   - `$XDG_STATE_HOME/opencode` when the variable is set;
   - `~/.local/state/opencode`;
   - on Windows only, `%LOCALAPPDATA%\opencode` and `%APPDATA%\opencode`.
2. In each, list `service.json` and `service-*.json`, newest first.
3. Parse `url`, `password`, `pid`, `version`. Skip a candidate whose `pid` is not alive (on
   Windows, `Process.GetProcessById`; on macOS, `kill(pid, 0)`), or whose file is being written.
4. `GET {url}/api/info` with a 2-second timeout, Basic auth when a password is present. The first
   candidate that answers wins.
5. Cache the endpoint for 30 seconds. Re-discover after any connection failure, and on every retry
   while disconnected. Never cache a failure longer than the backoff.

Auth header: `Authorization: Basic base64("opencode:" + password)`. If `password` is absent, try
unauthenticated — the client type makes it optional, even though every local service here has one.

Support `OPENCODE_SERVICE_FILE` as a test/override hook (an absolute path to one registration file).
It is documented for diagnostics, not exposed in Settings: the password must never be copied into
`settings.json`.

### Connection, reconnect and resync

One loop:

1. Discover the endpoint. If none, report `Disconnected` with a short reason, wait with backoff and
   go to 1.
2. **Resync before streaming.** `GET /api/session/active`; choose the session (below); fetch
   `GET /api/session/{id}`; fold the snapshot into state. If no session is active, keep the tracked
   one and mark it idle, or clear it if it no longer exists.
3. Open `GET /api/event?types=…` with the events in the mapping table (plus
   `server.connected`). Set `Accept: text/event-stream`, `HttpCompletionOption.ResponseHeadersRead`,
   and **no overall timeout**; cancellation is the only way out.
4. Parse SSE: concatenate multiple `data:` lines with `\n`, ignore `:` comments (heartbeats) and
   blank lines, and ignore the SSE `event:` field — the JSON's own `type` is authoritative.
5. Fold each event. Any event for a session other than the tracked one is ignored unless it is
   `session.created`, `session.deleted`, `session.renamed` or a model selection for the tracked
   session.
6. On EOF, HTTP error or socket error: report `Disconnected`, back off (1s, 2s, 4s … capped at 30s,
   with jitter), and resync from step 1. Backoff resets after a connection that stayed up for 30s.
7. `server.connected` means the stream is ready, not that the state is fresh — resync has already
   happened in step 2.

**Keepalive.** The 60-second limits refresh also re-checks `/api/session/active`. If the tracked
session is still running, `last_event_utc` is set to now. Without that, a long tool call that emits
no events for 15 minutes would let `SessionIdleTimeout` put the pet to sleep mid-build. The active
map is authoritative, so this cannot keep a finished turn awake.

The client never synthesises an idle on disconnect. A stream that dies does not mean the turn
ended; the next resync decides. `session_status` still decays to `Inactive` after the existing
15-minute `SessionIdleTimeout`, so a dead service cannot leave the pet "working" forever.

### Which session is tracked

`/api/session/active` returns foreground drains, keyed by session id, with `type: "running"`.
Sessions absent from it may still exist; they are just not executing.

1. Keep the current session if it is still active.
2. Otherwise consider only **root** sessions: `parentID` absent, or the parent is not itself a known
   session (a forked/sub-agent child must not drive the display).
3. Among roots, pick the one with the greatest `time.updated`.
4. Remember child ids in a bounded map (last 100) so their events can be ignored even after they
   disappear from the active list.
5. If no root is active, keep the tracked session (it may still be open, just idle). If it returns
   `404` from `/api/session/{id}`, clear the session and mark it ended.

A `session.created` with a `parentID` is a child: record it and ignore it. A `session.deleted` for a
child is ignored. A `session.deleted` for the tracked session sets `session_ended_utc` and clears
the activity to `Idle`.

### Mapping

The pure mapper takes `(current VitalsState, event, now)` and returns a new state, exactly like
`VitalsUpdates`. It is filesystem-free and clock-injected so both test suites can drive it from
shared fixtures.

| OpenCode event | `activity` | Other state |
|---|---|---|
| `session.execution.started` | `Working` | `last_event_utc` |
| `session.step.started` | `Working` | model, `last_event_utc` |
| `session.tool.called` | `Working` | `last_event_utc` |
| `session.text.started`, `session.reasoning.started` | `Working` | `last_event_utc` |
| `session.compaction.started`, `session.shell.started` | `Working` | `last_event_utc` |
| `session.retry.scheduled` | `Working` | Not `Waiting`: it is blocked on time/provider, not on the user |
| `session.tool.success`, `session.tool.failed`, `session.step.ended`, `session.step.failed` | `Working` | The turn continues until a terminal event |
| `permission.asked` | `Waiting` | `waiting_message` from `data.message`, else `data.action` |
| `permission.replied` | `Working` | Clears `waiting_message` |
| `session.idle` | `Idle` | Clears `waiting_message`; session stays open |
| `session.execution.succeeded`, `session.execution.failed`, `session.execution.interrupted` | `Idle` | Clears `waiting_message`; these end a turn, not the session |
| `session.status` with `status.type == "busy"` | `Working` | |
| `session.status` with `status.type == "idle"` | `Idle` | |
| `session.status` with `status.type == "retry"` | `Working` | Keep any existing message out of `waiting_message` |
| `session.created` | unchanged | Adopt a root session; set id, title, model |
| `session.renamed` | unchanged | `session_name` ← `title` |
| `session.model.selected` | unchanged | `model_display_name` |
| `session.usage.updated` | unchanged | `session_cost_usd` ← `cost`; tokens kept for context later |
| `session.usage.recorded` | unchanged | Not used: it reports a per-source increment, and treating it as a total would double-count |
| `session.deleted`, `session.archived` | `Idle` | `session_ended_utc` ← now |
| `session.unarchived` | unchanged | Clears `session_ended_utc` |
| anything else | unchanged | Log the type once; never throw |

Two details that matter:

- **Never put `permission.asked.resources` in `waiting_message`.** Those are file paths or command
  strings, and `state.json` is served unauthenticated on the LAN on Windows. The action name
  (`"bash"`, `"edit"`) and the message are enough for the card. This is a privacy requirement, not
  a style choice.
- **`session.idle` is not `SessionEnd`.** OpenCode has no per-conversation end event in normal use;
  it has idle executions and deleted sessions. Idle therefore sets `activity = Idle` and moves
  `last_event_utc`, exactly like Claude's `Stop`; only `session.deleted`/`archived` sets
  `session_ended_utc`.

Snapshot (`GET /api/session/{id}`):

| `Session.Info` | `VitalsState` |
|---|---|
| `id` | `session_id` |
| `title` | `session_name` |
| `model` resolved through `GET /api/model` | `model_display_name` (fallback: `model.id`) |
| `cost` | `session_cost_usd` |
| `time.updated` − `time.created` | `session_duration_ms` (approximate; documented as such) |
| session is in `/api/session/active` | `activity = Working`, `last_event_utc = now` |
| otherwise, `time.idle` (or `time.updated`) | `activity = Idle`, `last_event_utc = that instant` |

`GET /api/model` is cached for five minutes and indexed by `(providerID, id)` (fall back to
`modelID`). It is only needed for the human-readable name.

`context_used_percentage` stays **null** in v1. `Session.Info.tokens` is cumulative across the
session, not the current context window, and `/api/session/{id}/context` returns messages rather
than a percentage. Computing it from cumulative tokens and `Model.Info.limit.context` would
overstate badly. Leave it null and list it as a known limitation; revisit with per-message usage if
that proves reliable.

### Quota

`GET /api/experimental/usage/limits` (refreshed on connect and every 60 seconds):

| Response | `RateLimitWindow` |
|---|---|
| `go.rolling.percent` / `resetsAt` | `session` |
| `go.weekly.percent` / `resetsAt` | `week` |
| `go.monthly` | ignored — the contract has no month window |
| `budgets` | ignored — team budgets, not the user's plan |
| `go` absent | leave both null |

`resetsAt` is Unix milliseconds; `percent` is already 0–100. This is an **OpenCode Go** meter, not
Claude's 5-hour/7-day subscription quota, and the settings copy and `known-limitations.md` must say
so. If the user is not on OpenCode Go, the rings are absent rather than wrong.

Do **not** call `GET /api/credential`. It returns secret values (including OAuth access tokens), and
there is no need for it in this feature. Using it to reach Anthropic's usage endpoint would be a new
privacy promise and a new secret reader; it is deliberately deferred. See
[Open questions](#open-questions).

On Windows, when Source is OpenCode, the existing usage-API fallback must not run: it writes
`week_sonnet` and `usage_api_last_success_utc`, which belong to the Claude source and would
contaminate `/status`. Pass `EnableUsageApiFallback = false` for the lifetime of the OpenCode
source, and null those two fields on every source switch.

### Field ownership and switching

| Field | Claude | OpenCode |
|---|---|---|
| `session`, `week` | statusLine | `go.rolling`, `go.weekly` |
| `week_sonnet`, `usage_api_last_success_utc` | usage API only | always null |
| `context_used_percentage` | statusLine | null in v1 |
| `model_display_name`, `session_id`, `session_name`, `session_cost_usd`, `session_duration_ms` | hooks | snapshot + events |
| `activity`, `activity_changed_utc`, `waiting_message`, `last_event_utc`, `session_ended_utc` | hooks | stream |
| `month_cost_usd` | always null | always null |

When the preference changes, the app:

1. stops the running source;
2. writes **one blank state** through `VitalsStateStore.Update` (activity `Unknown`, no session,
   no quota, no waiting message), so neither source's data is shown under the other's name;
3. starts the new source (OpenCode: discovery + stream; Claude: nothing to start — hooks remain
   registered and the next event populates the file);
4. on Windows, restarts the HTTP host with the usage-API fallback gated to the new source.

The blank state is a deliberate data loss of at most one refresh. It is better than a Claude
session name and cost appearing beside an OpenCode session, or vice versa.

Hooks stay registered on both platforms. That is what keeps Claude Code's own status line working
when Source is OpenCode and makes switching back seamless. The guard is in the **write**, not the
registration:

- **statusLine verb:** fold the payload into a local in-memory state and print the line as usual,
  but skip `VitalsStateStore.Update` when Source is not Claude. Claude Code's status line therefore
  keeps showing Claude's model/usage instead of OpenCode's.
- **lifecycle and session verbs:** skip the store write and exit 0. No output is involved.

This preserves the two hard rules for the hook process: it never touches the network and it always
exits 0. Reading `settings.json` is one local file read, already the cheapest kind of I/O.

---

## Settings and UI

New preference, in both `AppSettings` types:

```json
{ "source": "claude" }
```

Allowed values are `"claude"` (default when missing, unknown or unreadable) and `"opencode"`.
C# uses `VitalsSource` with explicit JSON member names; Swift uses
`enum VitalsSource: String { case claude, opencode }`. A new `SourcePreference` wrapper mirrors the
existing preference classes, and `docs/SPECS.md` gains a row.

**Windows — Settings → Source** (a sixth nav page; the nav has room for six). A two-choice group
("Claude Code" / "OpenCode"), a status line and a **Retry** button:

- `Connected to 127.0.0.1:49374 (OpenCode 2.2.5)`
- `OpenCode is not running` / `OpenCode service not found`
- `Connected, but the API version is untested` (when `/api/info.version` is not 2.2.x)

No URL or password field. Discovery is automatic; the password never enters `settings.json`.
Changing the source goes through `TrayIcon.SetSource`, which owns start/stop, the blank state and
the HTTP/usage-API gate.

**macOS — Settings → Source** (a tab between Usage and Advanced). Same choice, same status line,
same Retry. The tab also explains that the Mac build reads only the OpenCode Go meters and never
Claude's quota. `SettingsModel` gains `source` and `setSource`, and `MenuBarController` owns the
source's lifecycle.

While OpenCode is selected and disconnected, the pet shows the existing `Unknown`/`Idle` poses and
the tooltip says the source is unavailable. No new state field is added for connection status; it
is settings-window and tooltip text only.

### macOS App Transport Security

The service is plain HTTP on loopback. The app's generated `Info.plist` should gain:

```xml
<key>NSAppTransportSecurity</key>
<dict>
    <key>NSAllowsLocalNetworking</key>
    <true/>
</dict>
```

That permits local networking without weakening ATS for public hosts. The app is not sandboxed, so
reading `~/.local/state/opencode/service.json` needs no entitlement. Verify with a release-signed
build, not just `swift run`: ATS behaviour is a bundle property.

---

## Work plan

### Phase 0 — Spikes (half a day)

- [ ] Run a real OpenCode turn and record the exact event order: prompt → execution → tool →
      permission → reply → idle. Save the raw payloads for fixtures.
- [ ] Confirm whether `session.status` (`busy`/`idle`/`retry`) is emitted live on 2.2.5. If not,
      note it and rely on the execution/idle events.
- [ ] Confirm a child/sub-agent session reports `parentID` in `session.created` and
      `Session.Info`.
- [ ] Verify the Windows registration-file path on a real Windows machine, including the
      `%LOCALAPPDATA%` vs `%USERPROFILE%\.local\state` question and the `service-<channel>.json`
      variants.
- [ ] Kill and restart the OpenCode service mid-session; confirm the port changes and the client
      can re-discover it.
- [ ] Confirm SSE behaviour with no events: heartbeat cadence and whether the server closes idle
      streams.

### Phase 1 — Contract, preference and fixtures (half a day)

- [ ] `VitalsSource` + `SourcePreference` in C# (`Core/State`) and Swift (`Core`).
- [ ] `AppSettings.Source` with a default of Claude; round-trip tests for a missing, unknown and
      valid value.
- [ ] Hook write guard on both platforms, including the statusLine local-fold path.
      Tests: Source = OpenCode writes nothing; Source = Claude behaves exactly as before;
      statusLine still prints a line under both.
- [ ] Blank-state helper (`VitalsState.Blank()` / `VitalsState.blank`) used by both hosts.
- [ ] `shared/fixtures/opencode-folds.json`: a deterministic `now`, a sequence of events and
      snapshots, and the expected fields after each step. Wire it into
      `SharedFixtureTests` on both sides.

**Done when:** every existing test passes untouched with the default Claude source; the hook guard
tests pass; the new fixture runs on both platforms.

### Phase 2 — macOS source (one and a half days)

- [ ] `OpenCode/OpenCodeDiscovery.swift` — candidate directories, `service*.json` parsing, pid
      check, `/api/info` probe, 30-second cache, `OPENCODE_SERVICE_FILE`.
- [ ] `OpenCode/OpenCodeSSE.swift` — a line parser with its own tests (multi-line data, comments,
      CRLF, blank lines, oversized lines).
- [ ] `OpenCode/OpenCodeClient.swift` — `URLSession` actor: Basic auth, snapshot calls, event
      stream via `bytes(for:)`, cancellation, 2-second request timeouts for discovery and infinite
      for the stream.
- [ ] `OpenCode/OpenCodeEvent.swift` — decodable envelopes; `data` decoded per event type, unknown
      types tolerated.
- [ ] `OpenCode/OpenCodeStateMapper.swift` — pure, clock-injected; snapshot and event folds.
- [ ] `OpenCode/OpenCodeUsage.swift` — limits → `RateLimitWindow`; 60-second refresh.
- [ ] `OpenCode/OpenCodeSource.swift` — discovery/stream/resync loop, backoff, `Status` enum,
      writes through `VitalsStateStore`.
- [ ] `MenuBarController` owns start/stop, listens for status changes, and refreshes the menu
      tooltip. `SourcePreference` is read at launch.
- [ ] Settings → Source tab; `SettingsModel.source` / `setSource`.
- [ ] `Info.plist` ATS key in `scripts/build-app.sh`.
- [ ] Tests: discovery (temp dirs, channel filenames, dead pid, malformed JSON, missing password),
      mapper (every row in the table, unknown events, child sessions, out-of-order events,
      permission privacy), usage mapping, and a `URLProtocol`-stubbed end-to-end stream.

**Done when:** with Source → OpenCode, a real session drives the pet through working, waiting and
idle; killing the service shows "not running" and reconnecting recovers without an app restart;
switching back to Claude leaves the next hook writing normally; the ATS key is present in the
built `.app`.

### Phase 3 — Windows source (one and a half days)

Mirror Phase 2 in C#:

- [ ] `Core/Sources/OpenCode/OpenCodeDiscovery.cs` — discovery (including the Windows candidate
      directories and `Process.GetProcessById`).
- [ ] `Core/Sources/OpenCode/OpenCodeSseParser.cs` — pure parser, unit tested.
- [ ] `Core/Sources/OpenCode/OpenCodeClient.cs` — `HttpClient` with an injectable
      `HttpMessageHandler` for tests; `ResponseHeadersRead`; infinite stream timeout.
- [ ] `Core/Sources/OpenCode/OpenCodeEvent.cs` — `JsonDocument`/record envelopes; tolerate unknown
      types.
- [ ] `Core/Sources/OpenCode/OpenCodeStateMapper.cs` — pure, `DateTimeOffset` injected.
- [ ] `Core/Sources/OpenCode/OpenCodeUsage.cs`.
- [ ] `Core/Sources/OpenCode/OpenCodeSource.cs` — loop, backoff, status event, writes through
      `VitalsStateStore.Default`.
- [ ] `TrayIcon`: `SetSource`, start/stop, status for the settings window, tooltip text.
- [ ] Settings → Source page and nav re-index (pages shift after the new page).
- [ ] Gate `EnableUsageApiFallback` off while Source is OpenCode; restart the host on a switch.
- [ ] Tests: discovery, parser, mapper, usage, and an end-to-end client test with a fake handler.

**Done when:** the Windows tray matches the macOS behaviour; `/status` serves the OpenCode state
with the **unchanged** schema; the usage-API fallback cannot write Claude fields while OpenCode is
selected; `dotnet test` is green.

### Phase 4 — Docs and release (half a day)

- [ ] `docs/privacy.md`: the app reads `service.json` (a local password), talks only to the
      loopback OpenCode API, reads session titles/model/cost and OpenCode Go meters, and never
      sends any of it anywhere. State explicitly that `/api/credential` is not called and that no
      Anthropic token is read.
- [ ] `docs/how-it-works.md`: add OpenCode to **Data sources** and update the diagram.
- [ ] `docs/status-endpoint.md`: no schema change, but say the values can come from either source.
- [ ] `docs/SPECS.md`: the `source` setting, its default, and the mapping/quota rules.
- [ ] `docs/known-limitations.md`: experimental API, live-only stream, no context percentage, no
      Anthropic quota through OpenCode, OpenCode must already be running, single tracked session.
- [ ] `docs/install.md`: OpenCode 2.2.x prerequisite and where the service file lives.
- [ ] `README.md`: one feature bullet ("Works with Claude Code or OpenCode").
- [ ] `docs/tray.md` and `docs/macos-port.md`: the new Source settings.
- [ ] `docs/license.md`: no new dependency is expected; confirm.
- [ ] Bump `<Version>` in `Directory.Build.props` (minor) and follow the normal release process.

---

## Tests

**Unit (both platforms).** Discovery, SSE parsing, mapping, quota, preference round-trip, hook
guard. The mapper and parser are pure and take an injected clock, so no test touches the network or
the real state file.

**Shared fixtures.** `shared/fixtures/opencode-folds.json` is run by `SharedFixtureTests` on both
sides, like `folds.json`. Cases:

- execution start → tool call → permission asked → reply → idle;
- a turn that fails and one that is interrupted;
- a snapshot while busy (must read Working) and while idle (must read Idle);
- a child session's events interleaved with the root's (must be ignored);
- a deleted session (must set `session_ended_utc`);
- `go` absent (rings null), `go` present (rolling/weekly mapped), `limited` true.

**Manual matrix.** macOS 14+ and Windows 10/11; OpenCode not running; service restart with a port
change; permission prompt; a long tool call; two root sessions in different projects; a sub-agent
fan-out; switching source mid-turn; app restart mid-session; OpenCode upgrade to an untested
version. Check `state.json` against the existing schema and `GET /status` with the ESP32 payload
sample.

**Privacy check.** With Source → OpenCode, `lsof`/Little Snitch shows only loopback traffic. No
request to `/api/credential`, `api.anthropic.com` or any public host. `error.log` contains no
password and no `permission.resources`.

---

## Risks

| Risk | Mitigation |
|---|---|
| The API is experimental and can change under us | Pin and document the tested version; unknown events are ignored; every DTO is tolerant of missing fields; no hard failure on parse errors |
| `/api/event` has no replay | Always resync from `/api/session/active` + `/api/session/{id}` before consuming the stream |
| The port changes on service restart | Re-read the registration file on every reconnect; never cache a URL past a failure |
| Windows discovery path unverified | Probe both XDG-style and `%LOCALAPPDATA%`/`%APPDATA%` paths; `OPENCODE_SERVICE_FILE` escape hatch; Phase 0 gate |
| A second writer races the hooks | The hook guard makes writes single-source; `VitalsStateStore` already serialises and writes atomically |
| Stale Claude data shown under OpenCode | Blank state on every source switch; gate the usage-API fallback off |
| Secrets leak into `state.json` or logs | Never store the password; never copy `permission.resources`; never call `/api/credential` |
| macOS ATS blocks loopback HTTP | `NSAllowsLocalNetworking` in the generated `Info.plist`; test a signed bundle |
| Child sessions flap the display | Root-only selection with a bounded child-id map |

---

## Open questions

1. **Anthropic quota via OpenCode.** A user running Claude models through OpenCode still has a
   Claude subscription. Reusing `UsageApiClient` would require an Anthropic OAuth token from
   either `~/.claude/.credentials.json` (Keychain on macOS, a new prompt) or OpenCode's
   `/api/credential` (returns secrets). **Recommendation:** do not do this in v1. Show OpenCode Go
   meters only, and record the gap in `known-limitations.md`. Revisit only as an explicit opt-in
   with a privacy-doc change.
2. **Context percentage.** Deferred until per-message usage is confirmed to be the current window
   rather than a cumulative total.
3. **Should the app offer to launch OpenCode?** **Recommendation:** no. It is another app's daemon,
   with its own lifecycle and credentials; the status line and Retry button are enough.
4. **Multiple running services.** Prefer a live `pid`, then the `latest` channel, then the newest
   file. If this proves common, add a picker; do not add one speculatively.
5. **Should Source be per-machine or shared between Windows and Mac?** Each platform already has its
   own `settings.json`; keep it per platform, like `pet` defaults.

---

## Estimate

About **four to five focused days**: Phase 0 half a day, Phase 1 half a day, one and a half days per
platform, half a day of docs and the manual matrix. The mapping and tests are the bulk; the HTTP and
SSE plumbing is small in both languages.

If only one platform is wanted first, macOS is the better pilot: discovery, authentication and the
event stream are verified here, and its source then becomes the reference for the C# port. The
shared fixture should be written in Phase 1 regardless, so the second port cannot drift.
