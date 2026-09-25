# Contributing to BorisCodeStatus

Issues and pull requests are welcome. This is a small project, so a short note before a large change
saves both of us time: open an issue describing what you want to do and why.

Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md). Security
problems go through the [security policy](SECURITY.md), not a public issue.

## Before you change behaviour

Read the [Design notes](README.md#design-notes) and the conventions in [`CLAUDE.md`](CLAUDE.md).
Several of them exist because the alternative broke something:

- **The hook process must be fast and must never fail.** File I/O only, never network, and it always
  exits 0 — Claude Code cancels an in-flight statusLine script when the next event arrives.
- **Derived values are computed on read, not stored.** Nothing writes to `state.json` while a session
  is quiet, so a stored time-dependent value would never decay.
- **`~/.claude/settings.json` is never regenerated.** It holds unrelated user config; the merger
  read-modify-writes it and refuses to touch a file it cannot parse.
- **`VitalsState` is the `/status` wire contract.** ESP32 firmware keys off it directly, so adding,
  renaming or retyping a field is a compatibility decision.

## Building and testing

You need the .NET 10 SDK and the WiX v4 CLI. WiX is pinned to 4.0.5 on purpose — see the
[Build](README.md#build) section before proposing an upgrade.

```bash
dotnet tool install --global wix --version 4.0.5
dotnet build -c Release
dotnet test BorisCodeStatus.Core.Tests
```

> **Running a development build rewrites your real `~/.claude/settings.json`.** The tray registers
> whatever path it is running from, so a `bin\Debug\...` hook path can end up in your global
> settings. Use the tray's **Advanced → Re-register hooks** from an installed copy to put it back.

Anything time-dependent must take its instant as a parameter and use that one instant throughout,
so tests do not pass one day and fail the next.

## Pull requests

- `dotnet test` must pass; CI runs it on every pull request into `main`.
- **Keep the README current in the same pull request.** It documents behaviour, the `/status`
  payload and the release process, and is expected to match the code.
- **Update the README's [Privacy](README.md#privacy-what-it-reads-and-what-leaves-your-machine)
  section** if the change alters what is read, stored, served or sent. A new bundled dependency goes
  in its *Third-party components* table.
- **Raise `<Version>` in `Directory.Build.props`** if the change ships anything, by semver: major for
  anything that breaks existing users or ESP32 firmware, minor for backward-compatible features,
  patch for fixes. Changes to docs, CI or tests alone need no bump. The Release workflow reads this
  value and refuses to publish a version that already exists.
- Comments explain *why*, not *what*, and say openly what is unverified.

## Licensing

BorisCodeStatus is licensed under the [GNU General Public License v3.0 or later](LICENSE). By
contributing you agree that your contribution is licensed under the same terms.
