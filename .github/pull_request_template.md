## What and why

<!-- What does this change, and what problem does it solve? Link the issue if there is one. -->

## Checklist

- [ ] `dotnet test BorisCodeStatus.Core.Tests` passes
- [ ] README updated to match (behaviour, `/status` payload sample, release notes as relevant)
- [ ] README *Privacy* section updated, if this changes what is read, stored, served or sent
- [ ] `<Version>` in `Directory.Build.props` raised, if this ships anything — level chosen and why:
- [ ] No throwing path or network call added to `BorisCodeStatus.Hooks`
