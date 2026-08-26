## What this changes

<!-- One or two sentences. Link the issue if there is one. -->

## Why

<!-- What was wrong or missing. -->

## Checks

- [ ] `dotnet build GetMan.sln -c Release` succeeds
- [ ] `dotnet run --project tools/SelfTest -- --offline` passes
- [ ] `GetMan.exe --self-check` passes
- [ ] New user-facing text goes through `{loc:T …}` / `Loc.T(…)` and is present in all three
      language files
- [ ] Screenshots regenerated with `GetMan.exe --shots docs/images` if a view changed
