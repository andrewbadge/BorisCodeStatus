# Build

[← Back to the README](../README.md)


Requires the .NET 10 SDK and the WiX v4 CLI:

```bash
dotnet tool install --global wix --version 4.0.5
dotnet build -c Release
```

A Release build of the solution produces `BorisCodeStatus.Installer\bin\Release\BorisCodeStatus.msi` as a
normal build output — no separate packaging step.

> **WiX version:** pinned to **4.0.5**. WiX v7 requires accepting the Open Source Maintenance Fee
> EULA, which is a commercial licensing decision rather than a technical one. v4 has no such
> requirement. Revisit only if someone signs off on the OSMF terms.

Run the tests:

```bash
dotnet test BorisCodeStatus.Core.Tests
```

### Continuous integration

`.github/workflows/build.yml` builds and tests on every push to `main` and every pull request into
`main`. It builds and verifies the MSI too, so a broken installer fails the PR rather than the
release, but does not upload it — nothing uses a CI build's MSI. It never publishes a release.

Pushing repeatedly to a PR cancels the superseded run, so a burst of commits costs one build rather
than several. Runs on `main` are never cancelled, so every merged commit keeps its own result.

### Dependency updates

`.github/dependabot.yml` opens weekly pull requests for NuGet packages and GitHub Actions, so each
one is built and tested by `build.yml` before it can reach `main`; nothing merges automatically.
The xUnit test packages and the Actions are grouped into one PR each. **WiX is excluded** — it is
pinned to 4.0.5 for licensing reasons (see [Build](#build)), and the SDK, extensions and CLI must
move together, which a single package bump would break.

### Publishing a release

`.github/workflows/release.yml` is **run manually**: Actions → Release → *Run workflow*. It builds,
tests, verifies and publishes in one go, creating the tag itself so the tag and the MSI can never
disagree.

**The version is not an input.** It comes from `<Version>` in `Directory.Build.props` — the same
value stamped onto every assembly and shown in the tray menu — so a published release can never
claim a version the installed app disagrees with. To release:

1. Raise `<Version>` in `Directory.Build.props` and merge that.
2. Run the workflow.

The build takes no `-p:Version` override, so the file drives the MSI exactly as it drives a local
build; the verification step then checks that file all the way through to the MSI, rather than
checking the workflow against a value the workflow itself supplied.

| Input | Meaning |
|---|---|
| `prerelease` | Mark the GitHub Release as a pre-release |
| `force` | Publish even if the version is not higher than the latest release |

Releasing is deliberately manual rather than triggered by a tag push. Publishing under a permanent
version number is a decision, not a side effect of pushing a ref.

The version is validated before anything is built, because these failures are cheap to catch and
expensive to discover afterwards:

- **Not `major.minor.build`** — an MSI `ProductVersion` cannot carry a `-rc1` style suffix.
- **Any part above 65535** — MSI version fields are 16-bit and Windows silently truncates larger
  values, producing a package that upgrades unpredictably.
- **Tag already exists** — raise `<Version>` rather than quietly moving a published tag.
- **Not higher than the latest release** — `MajorUpgrade` will not replace an installed copy
  otherwise, so the release would install but never supersede anything. Override with `force` if
  that is genuinely intended.

The built MSI is then checked before publishing: the version was stamped, both executables are in
the payload, and `ALLUSERS` is unset — that last one is a regression guard, because setting it would
quietly turn this back into a package that demands administrator rights.

To build a versioned MSI locally:

```bash
dotnet build -c Release -p:Version=1.2.3
```

### Testing the hook by hand

```bash
echo '{"model":{"display_name":"Opus 5"},"rate_limits":{"five_hour":{"used_percentage":42}}}' \
  | BorisCodeStatus.Hooks.exe statusline

echo '{"hook_event_name":"Notification"}' | BorisCodeStatus.Hooks.exe notification

echo '{"hook_event_name":"SessionStart"}' | BorisCodeStatus.Hooks.exe sessionstart
echo '{"hook_event_name":"SessionEnd"}'   | BorisCodeStatus.Hooks.exe sessionend
```

Then check `%LOCALAPPDATA%\BorisCodeStatus\state.json`, or `curl http://localhost:5080/status` with the
tray running and the HTTP service enabled.

---

## Design notes

The full behavioural specification — every setting with its default, every timing, every file —
is [docs/SPECS.md](SPECS.md). This section is the reasoning behind it.

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
