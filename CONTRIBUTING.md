# Contributing to GetMan

Thanks for taking the time. Bug reports, translations and pull requests are all welcome.

## Building

You need the [.NET 9 SDK](https://dotnet.microsoft.com/download) on Windows 10 or 11. There is no
other prerequisite — no Node, no npm, no native toolchain.

```
git clone https://github.com/SharofSoliyev/getman.git
cd getman
dotnet build GetMan.sln -c Release
dotnet run  --project src/GetMan
```

To produce the single-file executables:

```
dotnet publish src/GetMan     -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist
dotnet publish src/GetMan.Cli -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist-cli
```

The solution holds three projects: the WPF app, the console runner (`src/GetMan.Cli`, which shares
`Models/` and `Services/` **by source**, because referencing a WPF `WinExe` would pull the whole
interface into a console tool) and the headless test suite.

That arrangement only works while the model and service layers stay free of WPF. If you add a
`using System.Windows…` to anything under `Models/` or `Services/`, the CLI and the tests stop
compiling — which is the point. Put the WPF-facing part in `Controls/`, the way
`Controls/LocExtension.cs` holds the `{loc:T …}` markup extension while the string table itself
lives in `Services/Localization.cs`.

The app targets `net9.0-windows`; the CLI and the test suite target plain `net9.0` and run on Linux
and macOS too. CI runs the whole service suite and the CLI on Ubuntu on every commit, so a
Windows-only API reaching those layers fails the build rather than the download.

A Windows-only call that genuinely belongs there — the registry lookup in `PostmanDiscovery`, DPAPI
in `SecretVault` — goes behind `OperatingSystem.IsWindows()`, with a documented fallback for the
other platforms. Do not simply suppress CA1416; the analyser is the thing that noticed.

## Before you open a pull request

Three checks have to pass. All are fast, and only the online half of the first needs a network.

```
dotnet build GetMan.sln -c Release

dotnet run --project tools/SelfTest -- --offline    # 175 assertions over the service layer
dotnet run --project tools/SelfTest                 # the same plus live HTTP

src/GetMan/bin/Release/net9.0-windows/GetMan.exe --self-check

src/GetMan.Cli/bin/Release/net9.0/getman.exe \
  run tools/fixtures/offline-smoke.postman_collection.json -r junit -o cli.xml   # must exit 1
```

The CLI fixture points at a closed local port, so it exercises import, run, report and the exit code
without needing anything to answer.

`--self-check` builds every window, verifies the language tables, and then drives the interaction
flows a user actually performs — rename focus, creating folders and requests, search filtering,
drag-and-drop reparenting, closing tabs, loading a workspace with an active environment, and the
theme round trip. It runs against a sandboxed workspace in `%TEMP%`, never your own data, and exits
non-zero on any failure.

If you change a view, regenerate the documentation screenshots so the README keeps up:

```
src/GetMan/bin/Release/net9.0-windows/GetMan.exe --shots docs/images
```

That renders from a seeded sandbox, so no real collection names or file paths end up in the images.

## Adding or fixing a language

Every user-visible string lives in one JSON file per language under `src/GetMan/Assets/Lang/`.
Keys are derived from the English text, so the same wording shares one entry everywhere.

To correct a translation, edit the value in `ru.json` or `uz.json` and run `--self-check`.

To add a language:

1. Copy `src/GetMan/Assets/Lang/en.json` to `<code>.json`, where `<code>` is the two-letter
   ISO 639-1 code, and translate the values. Leave the keys alone.
2. Add one entry to `Loc.Languages` in `src/GetMan/Services/Localization.cs`:
   `new("<code>", "<name in that language>", "<name in English>")`.
3. Run `--self-check`. It compares every table against English and fails on a missing or extra key,
   so a half-finished translation cannot ship quietly.

The file is embedded automatically — `GetMan.csproj` globs `Assets/Lang/*.json`.

In XAML, use the markup extension rather than a literal:

```xml
<TextBlock Text="{loc:T s.send}" />
```

In C#, use `Loc.T("s.key")`, or `Loc.T("s.key", arg0, arg1)` when the string has `{0}` placeholders.
Bindings read the table through an indexer, so switching language repaints the whole window without
a restart; a plain string assigned in code does not, which is why the status line is re-rendered by
hand when the language changes.

## Code style

Match the file you are editing. A few conventions that run through the project:

- Comments explain **why**, not what. If a line looks odd, say what would break without it.
- `[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit.Mvvm rather than hand-written
  boilerplate.
- Colours, fonts, spacing and motion come from the token dictionaries in `src/GetMan/Themes/`.
  Do not hard-code a hex value in a view.
- Never animate a brush. Brushes handed to a `Setter` are frozen when the style is sealed, and
  animating one throws at run time — which neither the compiler nor `--self-check` will catch.
  Animate `Opacity` or a transform, or layer an overlay.
- New user-facing text goes in the language files, not in a string literal.

## Cutting a release

Releases build themselves. Push a tag and that is the whole procedure:

```
git tag v1.1.0
git push origin v1.1.0
```

`.github/workflows/release.yml` then runs the full CI workflow first — a release that cannot pass
the checks every commit passes never gets published — and only afterwards builds the two
executables, stamped with the version from the tag.

Before it publishes anything it checks the binaries it is about to upload: the CLI has to report
exactly the tagged version, and the app has to pass `--self-check`. So the files people download
are the files that were tested, not an earlier build of the same commit.

The release gets six assets and a `SHA256SUMS.txt`: the Windows app and CLI, and the CLI for
linux-x64, linux-arm64, osx-x64 and osx-arm64. The cross-platform builds are produced on an Ubuntu
runner, and the linux-x64 one is executed from an empty directory before publishing — the others are
built identically and checked for stray files, which is as far as this goes without a matrix of
hosts. Notes are generated from the commits since the previous tag.

- A tag with a hyphen (`v1.1.0-rc.1`) is published as a **pre-release**, per semver.
- Re-running the job on a tag that already has a release replaces the assets instead of failing,
  so a flaky upload can just be retried.
- To cut one without tagging locally, run the **Release** workflow from the Actions tab and give it
  a version; it creates the tag at the commit it built.

Nothing else needs its version bumped — `<Version>` in the two csproj files is only the fallback
for a local build, and `-p:Version=` from the tag overrides it. `Settings → About` and
`getman --version` both read the stamped value, which is what a bug report should quote.

## Reporting a bug

Open an issue with the GetMan version, your Windows version, what you did, what happened, and what
you expected. If GetMan wrote a crash log it is at `%APPDATA%\GetMan\crash.log` — please attach it,
after checking it for anything private.

If the bug involves a specific collection, a trimmed-down export that still reproduces it is worth
more than a description.

Security problems: see [SECURITY.md](SECURITY.md) — please do not open a public issue for those.
