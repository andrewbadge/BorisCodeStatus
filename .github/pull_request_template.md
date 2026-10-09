## What and why

<!-- What does this change, and what problem does it solve? Link the issue if there is one. -->

## Checklist

- [ ] `dotnet test BorisCodeStatus.Core.Tests` passes
- [ ] README and `docs/` updated to match (behaviour, `/status` payload sample in `docs/status-endpoint.md`, release notes as relevant)
- [ ] `docs/privacy.md` updated, if this changes what is read, stored, served or sent
- [ ] `<Version>` in `Directory.Build.props` raised, if this ships anything — level chosen and why:
- [ ] No throwing path or network call added to `BorisCodeStatus.Hooks`
