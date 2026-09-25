# Security policy

## Supported versions

Only the [latest release](https://github.com/andrewbadge/BorisCodeStatus/releases/latest) receives
fixes. Upgrading is a matter of installing the newer MSI over the old one.

## Reporting a vulnerability

Please **do not open a public issue**. Report it privately through GitHub instead:
**Security → [Report a vulnerability](https://github.com/andrewbadge/BorisCodeStatus/security/advisories/new)**.

Include what you found, how to reproduce it, and the version you tested. This is a one-person
project maintained in spare time, so there is no guaranteed response time, but reports are taken
seriously and you will be credited in the advisory unless you prefer not to be.

## Known and by design

These are documented trade-offs rather than vulnerabilities, though a report that shows one being
worse than the README describes is still welcome:

- **`/status` is unauthenticated** and, when the HTTP service is switched on, serves session names,
  cost and usage percentages to anything on the LAN. The service is **off by default**. See
  [Security: the endpoint is unauthenticated](README.md#%EF%B8%8F-security-the-endpoint-is-unauthenticated).
- **The usage-API fallback reads the Claude Code OAuth token** to call `api.anthropic.com`. It is
  opt-in and off by default, and the token is sent nowhere else. See
  [Privacy](README.md#privacy-what-it-reads-and-what-leaves-your-machine).
