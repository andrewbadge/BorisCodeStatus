# macOS port: proposal

[← Back to the README](../README.md)


**Status: in progress.** Steps 1 to 3 of the [order of work](#order-of-work) exist in
[`macos/`](../macos): the `BorisCodeStatusCore` Swift package, the `BorisCodeStatusHooks` helper,
the shared fixtures, and a menu-bar app that draws the pet from live state. Nothing registers the
hooks yet, and there are no cards, sounds or settings window. This page records how the macOS
version is built, what it deliberately leaves out, and which Windows decisions carry over
unchanged. As parts ship, they move into the regular docs (`install.md`, `privacy.md`, `tray.md`,
`known-limitations.md`) and this page shrinks to the reasoning.

```bash
cd macos
swift build                                   # every target
swift test                                    # needs Xcode; Command Line Tools lack swift-testing's macros
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test   # if xcode-select still points at them
scripts/build-app.sh                          # universal, ad-hoc signed .build/BorisCodeStatus.app
scripts/build-app.sh --debug                  # this machine's architecture only, faster
echo '{"hook_event_name":"Notification"}' | .build/debug/BorisCodeStatusHooks notification
```

Running the helper by hand writes the real `~/Library/Application Support/BorisCodeStatus/state.json`,
which a running app picks up at once. That is harmless, since only this app reads the file. It never
touches `~/.claude/settings.json`, which only the app's registration does.

The pet sprites in `SpriteData.swift` are generated from the Windows build's `DogSprites.cs` by
script rather than retyped, and a test checks every grid is 16 by 16 and only uses colours its
palette defines. Edit both files together.

---

## Decisions

| Decision | Choice | Why |
|---|---|---|
| Implementation | **All native Swift** — no .NET on the Mac | One signed `.app`, native start-up for the hook, no runtime to bundle or notarise. A hybrid (the .NET `Core`/`Hooks` with a Swift menu bar) was considered and rejected: it would bring back two processes to manage and a mixed bundle that is harder to sign. |
| HTTP service | **Not included** | The Mac version is a standalone menu-bar app. No `/status`, no listener, no ESP32 relay. |
| Usage API fallback | **Not included** | On Windows it only runs while the HTTP service is on, because `/status` is the only consumer of `week_sonnet`. Without it there is no reason to read the OAuth token — which on macOS lives in the Keychain and would mean a Keychain access prompt. |
| Names | **No spaces anywhere** | The hook's path is written into a shell command in `~/.claude/settings.json`. See [Naming](#naming). |
| Bundle identifier | `au.getboris.BorisCodeStatus` | |
| Default pet | **The cat** | Windows defaults to the dog. Only the default differs: `settings.json` stores the same `pet` names on both, and every pet is available on both. |

What dropping HTTP and the usage API takes with it: `FirewallGuard`, the *Enable HTTP service*,
*Use usage API for Sonnet quota*, *Open in browser* and *Fix firewall access…* settings,
`VitalsApi`, `VitalsApiHost`, `UsageApiRefreshService` and `UsageApiClient`. There is no listener,
so there is no app-firewall prompt and no Local Network privacy question.

The privacy position on the Mac is therefore simpler than on Windows: **the only network call the
app can make is the opt-in update check to `api.github.com`**, and it never reads the OAuth token.

---

## Naming

| Item | Name |
|---|---|
| App bundle | `BorisCodeStatus.app` |
| Bundle identifier | `au.getboris.BorisCodeStatus` |
| Hook helper | `BorisCodeStatus.app/Contents/Helpers/BorisCodeStatusHooks` |
| Hook helper identifier (signing) | `au.getboris.BorisCodeStatusHooks` |
| Swift package | `BorisCodeStatusCore` |
| Registered command | `/Applications/BorisCodeStatus.app/Contents/Helpers/BorisCodeStatusHooks statusline` |

The data directory, `~/Library/Application Support/BorisCodeStatus`, does contain a space. That is
harmless: only the app and the hook open it, through Swift file APIs, and it never appears in a
shell command. The settings merger still quotes the registered path, because a user can install the
app somewhere unusual.

---

## Architecture

```
Claude Code ──stdin JSON──▶ BorisCodeStatusHooks ──writes──▶ ~/Library/Application Support/BorisCodeStatus/state.json
                            (runs once per event,                         │ DispatchSource (watches the directory)
                             then exits 0)                                ▼
                                                           BorisCodeStatus.app (menu bar)
```

The same shape as Windows minus the listener: one short-lived hook process and one long-lived app
sharing one atomically written JSON file, with no IPC.

### What carries over unchanged

These are rules, not Windows details, and the Mac version keeps every one of them:

- **The hook must be fast and must never fail.** File I/O only, never network; a 3-second
  watchdog; it always exits 0, even on malformed input or an unknown verb.
- **Derived values are computed on read, not stored**, and anything time-dependent takes its
  instant as a parameter (`sessionStatus(at:)`, `DogStates.for(state, now:)`).
- **`activity` and `session_status` stay independent.**
- **Side effects fire on transitions**, keyed off `activity_changed_utc`, never on "state is
  currently X".
- **`~/.claude/settings.json` is read-modify-written, never regenerated.** Backed up once, a
  third-party `statusLine` left alone with a warning, an unparseable file left untouched.
- **Preferences live in one `settings.json`** whose defaults need no first-run write. The
  member names match Windows (`AppSettings.swift`, `SettingsStore.swift`, `Preferences.swift`);
  a member with a bad value falls back to its default on its own rather than resetting the file.
- **The `state.json` schema stays identical to Windows.** Nothing external reads it on the Mac, but
  keeping it the same lets both implementations share test fixtures (see
  [Keeping the two in step](#keeping-the-two-in-step)).

### Windows pieces and their Mac equivalents

| Windows | macOS | Notes |
|---|---|---|
| `BorisCodeStatus.Hooks.exe` | `BorisCodeStatusHooks` Swift command-line tool | Native start-up takes a few milliseconds, which matters because Claude Code cancels a slow statusLine command. |
| `BorisCodeStatus.Core` | `BorisCodeStatusCore` Swift package | Models, `VitalsUpdates`, `DogStates`, session status, `ClaudeSettingsMerger`, `SettingsStore`, `UpdateChecker`. |
| `%LOCALAPPDATA%\BorisCodeStatus` | `~/Library/Application Support/BorisCodeStatus` | Only the paths type changes. |
| Named `Mutex` + atomic replace | `flock()` on a `.lock` file, then write a temp file and `rename()` | Same semantics. |
| `FileSystemWatcher` | `DispatchSource` file-system source **on the directory** | The atomic rename swaps the file's inode, so a source on the file itself goes dead after the first write. |
| `NotifyIcon` + `TrayIconRenderer` | `NSStatusItem` with a non-template `NSImage` | Drawn at 16px (1x) or 32px (2x), whole-pixel integer scales only, `interpolation = .none`. |
| `DogSprites`, `PixelFont` | Ported unchanged as string grids | The sprite data stays diffable text. |
| `WaitingCard`, status card | `NSPanel` with `.nonactivatingPanel` at `.floating` level | Never takes focus from the terminal, which Windows needed extra code for. |
| `SettingsWindow` | SwiftUI window | Pages: Notify, Card, Usage, Advanced, About. Pixel widgets drawn in `Canvas`. |
| `WindowActivator` | `NSWorkspace.openApplication(at:configuration:)` with `activates = true` | Plain `NSRunningApplication.activate()` from a background app is often ignored on macOS 14 and later. Raising a specific *window* needs the Accessibility permission, so v1 raises the app only. |
| `WaitingSound` | `NSSound` | The same bundled WAV files. |
| HKCU `Run` key | `SMAppService.mainApp.register()` | Appears under System Settings → General → Login Items. |
| Single-instance mutex | Check `NSRunningApplication` for the bundle identifier | |
| WiX MSI | Notarised `.dmg`; a Homebrew cask later, optionally | |

### Settings on the Mac

The **Advanced** page shrinks to repairs:

- **Re-register hooks** — as on Windows.
- **Unregister hooks** — new; see [Uninstalling](#uninstalling).
- The location of the error log.

**About** keeps *Check for updates* (`update_check_enabled`, off by default). Notify, Card and
Usage are unchanged. `http_enabled` and `usage_api_enabled` do not exist on the Mac.

---

## Mac-specific risks

### App Translocation: the new `bin\Debug` trap

macOS runs a quarantined app that has not been moved — for instance one opened straight from
Downloads — from a randomised read-only path under
`/private/var/folders/.../AppTranslocation/...`. If the app registers *that* path in
`~/.claude/settings.json`, every hook fails silently once the path disappears, because the hook is
built never to report errors. This is the same failure the Windows docs warn about for `bin\Debug`
builds.

So registration must refuse a translocated path, and also refuse build output under `DerivedData`
or `.build`. It should also warn whenever the registered path no longer exists. Installing to
`/Applications` is the supported route.

### Uninstalling

A Mac app is removed by dragging it to the Bin; there is no uninstaller to run cleanup. The stale
`statusLine` and `hooks` entries the Windows MSI already leaves behind would be the norm here, not
an edge case. Hence **Settings → Advanced → Unregister hooks**, and `install.md` should tell users to
use it before deleting the app.

### No sandbox, no App Store

The app has to write `~/.claude/settings.json`, which the App Sandbox forbids. It is distributed
directly, outside the Mac App Store.

### Signing and notarisation

Gatekeeper blocks an unsigned downloaded app. A public release needs a Developer ID certificate
(an Apple Developer Program membership, US$99 a year) and notarisation in the release workflow, with
the credentials held as repository secrets. Like the WiX 4.0.5 pin, that is a cost and licensing
decision rather than a technical one. Ad-hoc signing is enough for anyone building from source.

---

## Keeping the two in step

Two implementations of `VitalsUpdates`, `DogStates` and session status can drift apart. The guard
is shared test data rather than shared code:

- A repo-level `fixtures/` folder: `dog-states.json` (states and the expected pose and session
  status at a fixed instant), `folds.json` (hook sequences and the state they should produce) and
  `state-windows.json` (a file the Windows build actually wrote, which the Swift build must read).
- Both `BorisCodeStatus.Core.Tests` (`SharedFixtureTests.cs`) and the Swift tests
  (`SharedFixtureTests.swift`) run against it.
- The clock is always injected, so the fixtures pin an instant and never depend on today's date.

**Version.** `<Version>` in `Directory.Build.props` stays the single source of truth. A build step
stamps it into `CFBundleShortVersionString` and `CFBundleVersion`, and the Release workflow gains a
`macos` job that builds, signs and notarises the `.dmg` alongside the MSI.

---

## Order of work

1. `BorisCodeStatusCore` and the shared fixture tests.
2. `BorisCodeStatusHooks`.
3. The menu-bar icon and the directory watcher.
4. Cards, sounds and the settings window.
5. Hook registration with the translocation guard, plus **Unregister hooks**.
6. Login item, then signing and notarisation in CI.
7. Docs: a Mac section in `install.md`, the Mac position in `privacy.md` (no token, no network but
   the update check), `tray.md`, and `known-limitations.md` loses "Windows only". Any new bundled
   dependency goes in `license.md`; the plan above needs none.
