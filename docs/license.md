# License

[← Back to the README](../README.md)


Copyright (C) 2026 Andrew Badge.

This program is free software: you can redistribute it and/or modify it under the terms of the
**GNU General Public License** as published by the Free Software Foundation, either version 3 of
the License, or (at your option) any later version. It is distributed in the hope that it will be
useful, but **without any warranty**; without even the implied warranty of merchantability or
fitness for a particular purpose. See [`LICENSE`](../LICENSE) for the full text, which the installer
also places beside the executables as `LICENSE.txt`.

### Third-party components

The MSI bundles components that are not covered by this project's license:

| Component | License | Where |
|---|---|---|
| .NET runtime and ASP.NET Core | MIT | Self-contained in both executables |
| WiX Toolset v4 utility custom action (`Wix4UtilCA`) | MS-RL | Inside the MSI, used to close and launch the tray during install |
| Notification sounds, by [freesound_community](https://pixabay.com/users/freesound_community-46691455/) on Pixabay | [Pixabay Content License](https://pixabay.com/service/license-summary/) | Embedded in `BorisCodeStatus.Tray.exe` (`BorisCodeStatus.Tray/Sounds`) |

The WiX custom action runs only during installation and is not linked into the program. The test
suite additionally uses xUnit (Apache 2.0) and coverlet (MIT) at build time; neither ships.

## Trademarks

"Claude" and "Claude Code" are trademarks of Anthropic, PBC. They are used here only to say which
tool this project works with. This project is not affiliated with, endorsed by or sponsored by
Anthropic, and it relies on an undocumented Anthropic endpoint that may change or stop working at
any time.
