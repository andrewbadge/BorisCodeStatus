# Known limitations

[← Back to the README](../README.md)


- **Uninstall does not remove the hook entries from `~/.claude/settings.json`.** The Run key,
  installed files and Start Menu shortcut are all removed cleanly, but the `statusLine` and `hooks`
  entries remain and will point at a missing executable. Claude Code tolerates this (the commands
  simply fail), but the entries should be removed by hand, or restored from
  `settings.json.boriscodestatus.bak`. Automating this is a v1.1 item.
- **`month_cost_usd` is always `null`.** See above — no data source provides it.
- **`week_sonnet` is `null` unless you opt in to the usage API**, and even then depends on an
  undocumented endpoint whose response shape is not contractual. The client tries several plausible
  property spellings and returns `null` rather than guessing wrong. It may simply never populate.
- **No authentication on `/status`.** See the security note above.
- **Running a development build can hijack your real `~/.claude/settings.json`.** The tray registers
  whatever path it is currently running from. If `BorisCodeStatus.Hooks.exe` happens to sit next to the
  tray exe in `bin\Debug\...` or `bin\Release\...`, that throwaway build path is written into your
  global settings — and once the directory is cleaned, every hook silently fails forever, because
  the hook process is deliberately built never to report errors. The symptom is `/status` returning
  `null` for everything with nothing logged anywhere. **This has happened in practice.** Check with:
  ```powershell
  Select-String -Path "$env:USERPROFILE\.claude\settings.json" -Pattern "BorisCodeStatus.Hooks.exe"
  ```
  If the path points inside a `bin\` folder, reinstall the MSI or use **Settings → Advanced → Re-register hooks** to
  repoint it. A fix — refusing to register from a `bin\`/`obj\` path, and warning when
  the registered path no longer exists — is a v1.1 item.
- **The MSI has been installed and verified on a developer machine, not on a clean VM.** Confirmed
  on a real per-user install: `msiexec` exit code 0 with no UAC prompt, product registered and
  uninstallable, files in `%LOCALAPPDATA%\Programs\BorisCodeStatus`, HKCU Run key set, hooks
  re-registered to the install path automatically, and live session data served from `/status`.
  What that install did *not* cover: a machine with no prior BorisCodeStatus state, and the upgrade and
  uninstall paths. Those are still worth exercising on a fresh VM before distributing.
- **Windows only.** The tray, the installer and the firewall handling are all Windows-specific.
