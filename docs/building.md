# Building from source

[← Back to the README](../README.md)


Most people should install a release: the [Releases](https://github.com/andrewbadge/BorisCodeStatus/releases/latest)
page has a per-user MSI for Windows and a signed, notarised `.dmg` for macOS. Build it yourself to
try a change, or to run code you have read. The repo holds both apps:

```bash
git clone https://github.com/andrewbadge/BorisCodeStatus.git
cd BorisCodeStatus
```

`windows/` is the .NET solution, `macos/` the Swift package, and `shared/` the test fixtures and
sounds both use. `<Version>` in the root `Directory.Build.props` is the version of both builds.

> **A build you run registers its own hooks.** Each app writes its own path into
> `~/.claude/settings.json` on launch, replacing whichever copy registered before. Install what you
> build and run the installed copy. The Mac app refuses to register from a build folder; the
> Windows tray does not, so a `bin\Debug` path can take over — [`CLAUDE.md`](../CLAUDE.md#traps)
> has the check and the repair (**Settings → Advanced → Re-register hooks** from the installed app).

## Windows

**Requirements:** Windows 10 or 11 (x64), the .NET 10 SDK, and the WiX v4 CLI.

```bash
dotnet tool install --global wix --version 4.0.5
cd windows
dotnet build -c Release
```

WiX is pinned to 4.0.5 on purpose; [Build](development.md) says why. The Release build produces the
installer as an ordinary build output, at
`windows\BorisCodeStatus.Installer\bin\Release\BorisCodeStatus.msi`. Run it to install, exactly as
you would a downloaded one: per-user, with no UAC prompt.

Run the tests with `dotnet test BorisCodeStatus.Core.Tests`.

## macOS

**Requirements:** macOS 14 or later, on Apple silicon or Intel, and Xcode or its Command Line
Tools. The tests need Xcode itself: the Command Line Tools lack swift-testing's macros.

```bash
cd macos
scripts/build-app.sh                                   # universal, ad-hoc signed .build/BorisCodeStatus.app
ditto .build/BorisCodeStatus.app /Applications/BorisCodeStatus.app
open /Applications/BorisCodeStatus.app
```

Install to **Applications** and open it from there. A copy run from the build folder refuses to
register its hooks, because that path will not last. `scripts/build-app.sh --debug` builds for this
Mac only, which is faster; `scripts/package-dmg.sh` wraps the `.app` in a `.dmg` as a release does.

A build of your own is signed ad hoc rather than with a Developer ID. That is fine on the Mac that
built it; Gatekeeper would refuse to open it on another Mac, which is why releases are signed and
notarised by the Release workflow instead.

Run the tests with `swift test`, from `macos/`. If `xcode-select` still points at the Command Line
Tools, use `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test`.

## Next

- [Build](development.md): CI, dependency updates, publishing a release, and exercising a hook by hand.
- [macOS port](macos-port.md): how the Mac app is built and what it shares with Windows.
