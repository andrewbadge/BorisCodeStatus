# Privacy: what it reads, and what leaves your machine

[← Back to the README](../README.md)


Worth knowing before you install anything that hooks into Claude Code:

- **It reads** a few fields of the JSON Claude Code pipes to its hooks: model, usage percentages,
  cost, session id and name, the event name, and the text of permission prompts. Claude Code sends
  each hook its whole payload — for `UserPromptSubmit` that includes your prompt — but the hook
  deserialises only those fields and discards the rest unread. It never opens your transcripts or
  project files.
- **Out of the box it makes no network calls at all.** The only outbound calls it can make are two,
  each opt-in. The first:
  with **Settings → Advanced → Use usage API for Sonnet quota** ticked (off by default) and the HTTP
  service on, it **reads your Claude Code OAuth token** from `~/.claude/.credentials.json` for one
  purpose — calling `https://api.anthropic.com/api/oauth/usage`, the same account the token belongs
  to, at most once every 5 minutes, to fetch the one figure the hooks do not supply. The token is
  sent nowhere else and is never written anywhere by this app. With the setting off, the token file
  is never opened. See [data source 3](how-it-works.md#3-apioauthusage--opt-in-fallback).
  The second: with **Settings → About → Check for updates** ticked (off by default) it asks
  `https://api.github.com/repos/andrewbadge/BorisCodeStatus/releases/latest` for the newest release
  at start and every 6 hours. The request is anonymous — no token, no account, and nothing about
  your install or usage, only a fixed `User-Agent: BorisCodeStatus` — though GitHub sees your IP address, as
  any web request shows it. Nothing is downloaded or installed; the reply is compared with the running
  version and discarded, apart from the newest version already announced, which is kept in `settings.json`.
- **It serves**, once you switch the HTTP service on (it is off by default), the fields shown in
  the [`/status` sample](status-endpoint.md) to anything on your LAN, **without
  authentication**. That includes session names and cost. Read the
  security note under [The `/status` endpoint](status-endpoint.md) before using it on a network you do
  not trust.
- **It lists your open apps** — process names and their executable descriptions — only when the
  settings window opens or you switch to its **Notify** page, to offer them as choices for **When
  notification is clicked**. It keeps nothing
  from that list but the one process name you pick, and never sends or serves it. When a click
  brings that app forward, it reads the titles of that app's windows, to pick the one running
  Claude Code; the titles are compared and forgotten, never stored, sent or served.
- **It writes** `%LOCALAPPDATA%\BorisCodeStatus\` (state, preferences, and `error.log` — the stack
  trace of any unexpected error, kept to about 1 MB and never sent anywhere) and adds entries
  to `~/.claude/settings.json`, after backing that file up once.
- There is no telemetry and no analytics. The update check above is off unless you turn it on.
