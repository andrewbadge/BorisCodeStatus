# BorisCodeStatus — specification

What the app does, stated as behaviour: the inputs it reads, the state it keeps, what it shows and
serves, every setting with its default, and the timings involved. It describes **v1.3.0**.

The [README](../README.md) and the pages it links to explain *why* things are the way they are — the security posture, the
privacy promises, the release process. This document is the *what*; where the two meet, it links
rather than repeats. If code and this document disagree, the code is right and this document is a
bug.

---

## 1. Components

| Component | Kind | Role |
|---|---|---|
| `BorisCodeStatus.Hooks.exe` | Console exe, one run per event | Receives Claude Code hook payloads on stdin and updates the shared state file. |
| `BorisCodeStatus.Tray.exe` | WinForms tray app, long-running | Watches the state file; draws the tray icon, cards and settings window; hosts the HTTP endpoint. |
| `BorisCodeStatus.Core` | Library | Models, the state store, payload→state mapping, preferences, the settings.json merger. |
| `BorisCodeStatus.Api` | Library | The `/status` and `/health` endpoint, started and stopped by the tray. |

The two executables never talk to each other. They share one file,
`%LOCALAPPDATA%\BorisCodeStatus\state.json`, written atomically under a named mutex.

Installed per-user by an MSI to `%LOCALAPPDATA%\Programs\BorisCodeStatus`, with no administrator
rights. The tray starts at login.

## 2. Inputs: Claude Code hooks

On every launch the tray merges its hook registrations into `~/.claude/settings.json`. It is
idempotent: it never rewrites unrelated settings, it leaves a third-party `statusLine` alone, and
it refuses to touch a file it cannot parse.

| Claude Code event | Hook verb | Effect on state |
|---|---|---|
| `statusLine` | `statusline` | Usage figures: 5-hour and 7-day windows, model, cost, context, session id and name. Prints a status line back to Claude Code. |
| `UserPromptSubmit`, `PreToolUse` | `working` | `activity` → `Working` |
| `Notification` | `notification` | `activity` → `Waiting`; `waiting_message` ← the hook's `message` |
| `Stop` | `stop` | `activity` → `Idle` |
| `SessionStart` | `sessionstart` | Session marked open (`activity` untouched) |
| `SessionEnd` | `sessionend` | Session marked ended (`activity` untouched) |

The hook process:

- reads only the fields listed in [Privacy](privacy.md) and discards the rest unread;
- does file I/O only, never network;
- is killed by a 3-second watchdog;
- **always exits 0**, including on malformed input or an unknown verb. An unexpected exception is
  written to `error.log` (§10) and to stderr.

`activity_changed_utc` moves only when `activity` changes. `waiting_message` is cleared on leaving
`Waiting`.

## 3. State model

### Activity and session

- **`activity`**: what Claude is doing this turn. One of `Unknown`, `Working`, `Waiting`, `Idle`.
  A live conversation reads `Idle` most of the time, because `Stop` fires at the end of every turn.
- **`session_status`**: whether a session is open at all. One of `Unknown`, `Active`, `Inactive`,
  `Ended`. A session with no event for **15 minutes** (`VitalsState.SessionIdleTimeout`) reads
  `Inactive`.

The two are independent. The session hooks never change `activity`.

### Derived values

`age_seconds`, `resets_in_minutes` and `session_status` are computed when read, never stored,
because nothing writes the file while a session is quiet. Every time-dependent decision takes one
instant and uses it throughout.

### Pose

`DogStates.For(state, now)` lives in Core, so that the ESP32 can derive the same pose:

| Condition (first match wins) | Pose |
|---|---|
| `session_status` is `Ended`, `Inactive` or `Unknown` | Sleeping |
| `activity` is `Working` | Working |
| `activity` is `Waiting` | Waiting |
| `activity` is `Idle` for less than **5 minutes** (`DogStates.SleepAfter`) | Idle |
| otherwise | Sleeping |

The tray re-evaluates every **30 seconds** as well as on every state change, so the pet falls
asleep without any new event arriving.

## 4. Outputs: HTTP endpoint

**Off by default.** When on, it listens on `0.0.0.0`, port **5080**, overridable with the
`BORISCODESTATUS_PORT` environment variable. It is unauthenticated (see [Security](status-endpoint.md#%EF%B8%8F-security-the-endpoint-is-unauthenticated)).

| Route | Response |
|---|---|
| `GET /status` | The full state as JSON (below). |
| `GET /health` | `{ "ok": true, "utc": "…" }` |

`/status` is the wire contract for the ESP32: snake_case, one object. Top level:
`session`, `week`, `week_sonnet` (each `{ used_percentage, resets_at, resets_in_minutes }`),
`context_used_percentage`, `model_display_name`, `session_id`, `session_name`, `session_cost_usd`,
`session_duration_ms`, `month_cost_usd` (always `null`; no source), `activity`,
`activity_changed_utc`, `waiting_message`, `last_event_utc`, `session_ended_utc`,
`session_status`, `last_updated_utc`, `usage_api_last_success_utc`, `age_seconds`. [The `/status` endpoint](status-endpoint.md) has
a sample payload.

Adding a field changes the contract. Settings, the pet and the cards are tray-only and never
appear here.

## 5. Tray icon

- **Drawn at runtime** at the tray's real small-icon size (16 px at 100%), never resampled. The
  16 px sprite is integer-scaled where the size allows (×2 at 200%); otherwise it is centred.
- **Sprites** are palette-index grids in `DogSprites.cs`: four poses each for the dog, the cat,
  the sentry bot, the rubber duck and the goat. Each pose carries a badge: the dog's idle face has none; the cat's idle face has a green one.
  The cat's sleeping tray face is derived from its idle face; the designer's curled-up sleeping cat
  appears on the full status card only. The bot's and duck's sleeping faces are derived the same
  way (eye off; eye shut with a lavender badge), as their designs have none. The goat is the only
  animated pet: working and waiting each have two grids that differ only in the tongue (and the beard swinging to the
  other side), chosen by `DogSprites.WagFrame(now)` — 250 ms a frame, a pure function of the instant so
  the tray icon and every card wag in step. Idle its mouth is shut and, like asleep, it holds one frame
  (`DogSprites.Wags`).
- **Quota ring** behind the pet shows the 5-hour session used. It is drawn ⅛ of the icon thick
  (2 px at 16 px). Colour: green below 50%, amber from 50% to 75% inclusive, red above 75%. With no
  data it is a faint track only. It redraws on a change of pose, of pet, or of 5% bucket.
- **Tooltip**: `BorisCodeStatus — <activity>` plus the 5h and 7d figures, capped at 63 characters.

| Interaction | Result |
|---|---|
| Right-click | Menu: **BorisCodeStatus vX.Y.Z** (opens the GitHub repository), **Settings…**, **Exit**. |
| Double-click | Brings the chosen app (§7) to the front if one is set; otherwise shows the status card. |

## 6. Cards

All cards are borderless, always-on-top, non-activating tool windows: no taskbar button, and they
never take keyboard focus. Each has a faint **×** in the top-right that brightens on hover and
closes the card. Layout is a 320×240 "screen" (the ESP32 panel's size) in a bezel, scaled to the
monitor; the pixel art and pixel font are drawn only at whole-number scales.

| Card | Shown when | Content | Goes away |
|---|---|---|---|
| **Waiting** | `activity` becomes `Waiting` and *When waiting → Show a card* is on | Header `WAITING`; the dog's portrait with a blinking **!**, or the sitting cat; a heading inferred from the message (*PERMISSION NEEDED*, *INPUT NEEDED*, else *CLAUDE NEEDS YOU*); the hook's message; a `Y / N` hint for permission prompts. | After **12 s** (paused while hovered), on click, on ×, or as soon as `activity` leaves `Waiting`. |
| **Idle** | `activity` becomes `Idle` and *When idle → Show a card* is on | Header `IDLE`; the pet's idle sprite; *YOUR TURN*; "Claude has finished and is waiting for your next message." | After 12 s, on click, on ×, or as soon as `activity` leaves `Idle`. |
| **Status** | Double-click on the icon (no chosen app), or permanently while pinned | Header: the pose, in its badge colour; the pet's sprite for the pose (the curled-up cat when the cat sleeps); `5h N% used`; reset time, week figure, model, HTTP and notify state; a bar showing the session gauge in the ring's colours. | Unpinned: as the waiting card. Pinned: only via × (which also unpins) or the setting. |

**Transition rule.** The waiting and idle cards and sounds fire once per transition, keyed off
`activity_changed_utc`, never on "is currently X", because the tray refreshes on every state write
and every 30-second tick. A session that is already idle when the tray starts is not announced.

**Pinned status card** (*Card → Keep status card on screen*):

- stays up and redraws on every refresh;
- has no countdown;
- drags from anywhere on the card (the window reports itself as a caption; the × stays clickable);
- double-click brings the chosen app to the front;
- its top-left corner is saved after each drag and restored at the next start. If that position is
  no longer fully on screen, it is moved fully onto the working area of the nearest monitor.

**Mini status card** (*Card → Mini status card*): 236×56 design pixels, ⅔ the width and ⅕ the
height of the full card. It shows the pet, the state, `5H N%` and the gauge. It applies to both the
snapshot and the pinned card.

## 7. Notifications

Two moments, each with three independent settings:

| Setting | When waiting | When idle |
|---|---|---|
| Show a card | **On** | Off |
| Play a sound | Off | Off |
| Which sound | Dog: Panting (default) / Woof · Cat: Purr (default) / Meow · Bot: Chirp (default) / Clamp · Duck: Quack (default) / Fly away · Goat: Bleat (default) / Herd | same, chosen separately |

- The sound plays once, on the transition, alongside the card if both are on. A new sound stops
  one still playing.
- Picking a sound, or switching a sound on, plays it immediately. Switching a card on shows one.
- The sound choice is kept per moment and per pet, so switching pets keeps every choice.
- **Sounds** are four 16-bit PCM WAV recordings made for the project, embedded in the tray
  assembly and played with `System.Media.SoundPlayer`.

**When notification is clicked** applies to the waiting and idle cards. **Just dismiss** (the
default) only closes the card. Choosing an app also brings that app's first top-level window to the
front, restoring it if minimised. The choice is stored as a process name. If that process is not
running, a balloon says so. The list offers every process with a visible, titled, unowned
top-level window. It is gathered off the UI thread when the settings window opens.

## 8. Settings window

Opened from the tray menu's **Settings…**. There is one instance at a time; it is modeless,
borderless, dragged by the strip above the tiles, and closed by **Close**, ×, or Esc. It holds no
state: every control reads from and calls into the tray, and the tray refreshes it on every state
change.

- **Header**: the version (click to open the repository), then three live tiles. **Status** shows
  the pose in its colour, plus HTTP and notify state. **Session · 5h** and **Week · 7d** each show
  the figure, a 20-segment gauge in the ring's colours, and the reset time.
- **Notify**: *When waiting*, *When idle* (§7), *When notification is clicked*.
- **Card**: *Keep status card on screen*, *Mini status card*, *Who keeps you company?* (dog, cat, sentry bot, rubber duck, goat)
- **Advanced**: *Enable HTTP service*; *Use usage API for Sonnet quota*; **Open in browser**
  (`/status`); **Re-register hooks**; **Fix firewall access…** (disabled while HTTP is off).

## 9. Preferences

All files live in `%LOCALAPPDATA%\BorisCodeStatus\`. Preferences are never in `state.json` and
never served. A marker file always records the **non-default** choice, so a missing or unreadable
file means the default and there is no first-run write. Each preference is read once at startup
and kept in step by its control.

| File | Present means | Default (absent) |
|---|---|---|
| `http-enabled.flag` | HTTP service on | Off |
| `usage-api-enabled.flag` | Usage-API fallback allowed | Off |
| `notifications-disabled.flag` | Waiting card off | Waiting card on |
| `waiting-sound.flag` | Waiting sound on | Off |
| `dog-woof.flag` / `cat-meow.flag` | Waiting sound is woof / meow | Panting / purr |
| `idle-card.flag` | Idle card on | Off |
| `idle-sound.flag` | Idle sound on | Off |
| `idle-dog-woof.flag` / `idle-cat-meow.flag` | Idle sound is woof / meow | Panting / purr |
| `card-click-app.txt` | Holds the process name to bring forward | Just dismiss |
| `status-card-pinned.flag` | Status card pinned | Snapshot on double-click |
| `status-card-mini.flag` | Mini status card | Full card |
| `status-card-position.txt` | `x,y` of the pinned card, in screen pixels | Bottom-right of the primary working area |
| `pet.txt` | Holds the pet's name: `Cat`, `Bot`, `Duck` or `Goat` | Dog |
| `cat-person.flag` | Cat (read only when `pet.txt` is missing; removed by the next choice) | Dog |
| `bot-alternate.flag` / `duck-alternate.flag` / `goat-alternate.flag` (and `idle-` twins) | Bot / duck / goat plays its second sound | Its default |

The same folder also holds `state.json`, `usage-api.stamp` and `error.log`.

## 10. Errors

- **Tray**: unhandled exceptions on the UI thread are logged and the app keeps running. Exceptions
  on background threads (fatal) and unobserved task exceptions are logged too.
- **Hook**: its catch-all logs and still exits 0.
- **Log**: `error.log` takes a timestamp, where the exception was caught, the version, the process
  id and the full exception. Past about 1 MB it is rolled to `error.log.old`. Writing it never
  throws.
- Expected failures are handled where they happen and are not logged: a locked `settings.json`, a
  busy port, an unreadable preference.

## 11. Usage-API fallback

Opt-in (*Advanced → Use usage API for Sonnet quota*), and it only matters while the HTTP service is
on. It reads the OAuth token from `~/.claude/.credentials.json` and calls
`https://api.anthropic.com/api/oauth/usage`:

- 30 s after start, then every 10 minutes;
- never more than once per 5 minutes;
- after a rate-limit, it backs off for 30 minutes.

Switching it off clears the fields it fetched. This is the app's only outbound call ([Privacy](privacy.md)).

## 12. Limits

- Windows 10/11, x64, per user.
- The settings window is sized for the DPI of the monitor it opens on.
- At 125% and 150% scaling the 16 px tray sprite is centred rather than scaled.
- *When notification is clicked* brings forward the chosen app's window whose title looks like
  Claude Code's (a status glyph, then a space); with none, the front-most. The title format is
  observed, not documented. It brings the window forward, not the tab within it, and with two
  Claude windows the front-most wins.
- The pinned card's position is checked against the monitors when it appears, not when monitors
  change while it is showing.
