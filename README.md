<div align="center">

# GetMan

**A native Windows API client — the Postman alternative that is one `.exe`.**

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)](#)
[![Languages: 3](https://img.shields.io/badge/UI-English%20·%20Русский%20·%20O'zbekcha-38BDF8)](#interface-languages)

**English** · [Русский](README.ru.md) · [O'zbekcha](README.uz.md)

**[Download the latest release](https://github.com/SharofSoliyev/getman/releases/latest)** —
self-contained executables, no installer and no runtime to fetch.
Windows will warn on first run - [why, and what to do about it](#windows-protected-your-pc).

<img src="docs/images/main-dark.png" alt="GetMan running a request against postman-echo, showing the response body, tests and timings" width="900">

</div>

---

Built with **WPF on .NET 9** and themed with **WPF-UI** (Fluent 2).
No Electron, no Chromium, no background node process: one native `.exe` that starts instantly and
imports your existing Postman collections as-is.

```
dotnet run --project src/GetMan            # run from source
dotnet publish src/GetMan -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist        # single-file GetMan.exe
dotnet publish src/GetMan.Cli -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist-cli    # console getman.exe
```

Workspace data lives in `%APPDATA%\GetMan\workspace.json` (with a rolling backup), one file per
Windows user. Nothing is uploaded anywhere, and passwords, tokens and keys are encrypted on disk
with Windows DPAPI — see [SECURITY.md](SECURITY.md) for what that does and does not protect.

## Platforms

| | Windows | Linux | macOS |
|---|:---:|:---:|:---:|
| **GetMan** — the desktop app | ✅ x64 | ✕ | ✕ |
| **getman** — the [command-line runner](#running-collections-from-the-command-line) | ✅ x64 | ✅ x64, arm64 | ✅ x64, arm64 |

The app is WPF, and WPF is a Windows technology — there is no build of it for Linux or macOS, and
there will not be one without rewriting the interface on a cross-platform toolkit. That is a real
piece of work rather than a switch, so the honest answer today is Windows only.

The **command-line runner is cross-platform**, which is where it matters most: it exists to run
collections in CI, and most CI runs on Linux. It shares the model and service layers with the app
and nothing else — no WPF — and CI proves that by running the whole service suite and the runner
itself on Ubuntu on every commit, not just compiling them.

## Installing on Windows

Download `GetMan-<version>-win-x64.exe` from
[Releases](https://github.com/SharofSoliyev/getman/releases/latest) and run it. There is no
installer and nothing to unpack: it is one self-contained executable, and it keeps your workspace
in `%APPDATA%\GetMan`.

### "Windows protected your PC"

The first run shows a blue SmartScreen dialog whose only visible button is **Don't run**. Click
**More info**, then **Run anyway**. Windows remembers the answer, so you see it once.

It is not a virus warning, and it says nothing about what the file does. SmartScreen asks two
questions — who signed this, and how many people have already run it — and an unsigned executable
from a small project cannot answer either. Every unsigned download from every small project gets
the same dialog.

The dialog stops appearing for everyone only when the binaries carry a code-signing certificate,
which is a recurring cost rather than a build setting: Azure Trusted Signing is about $10 a month,
and a traditional OV certificate a few hundred dollars a year with a hardware token. That is the
honest reason it has not happened yet. The signing step is already in
[the release workflow](.github/workflows/release.yml); it stays skipped until the repository is
given the account secrets and turns itself on the moment it has them, so no release needs
rewriting when that day comes.

Until then, check the download instead of trusting the dialog. Every release ships
`SHA256SUMS.txt`:

```powershell
Get-FileHash .\GetMan-1.1.0-win-x64.exe -Algorithm SHA256
```

If the hash matches the line for that file in `SHA256SUMS.txt`, the executable is byte-for-byte
what GitHub Actions built from this repository — a build you can read, on a runner you can see the
log of. That is a stronger guarantee than the SmartScreen dialog would ever have given you.

## Interface languages

GetMan speaks **English, Russian and Uzbek**. Pick one from the app bar and every label, tooltip,
dialog and status message changes immediately — no restart. On first run GetMan follows the Windows
display language when it is one of the three, and falls back to English otherwise. The choice is
saved with the rest of your settings.

| | |
|---|---|
| <img src="docs/images/main-ru.png" alt="GetMan with the Russian interface" width="430"> | <img src="docs/images/main-uz.png" alt="GetMan with the Uzbek interface" width="430"> |
| Русский | O'zbekcha |

Every string lives in one JSON file per language under
[`src/GetMan/Assets/Lang/`](src/GetMan/Assets/Lang). Adding a fourth language means copying
`en.json`, translating the values, and adding one entry to `Loc.Languages` — see
[CONTRIBUTING.md](CONTRIBUTING.md). `GetMan.exe --self-check` fails the build if any language is
missing a key, so translations cannot silently drift.

## Screenshots

| Light theme | Collection runner |
|---|---|
| <img src="docs/images/main-light.png" alt="GetMan in the light theme" width="430"> | <img src="docs/images/runner.png" alt="The collection runner after a finished run" width="430"> |

| Environments | Settings |
|---|---|
| <img src="docs/images/environments.png" alt="The environment manager" width="430"> | <img src="docs/images/settings.png" alt="The settings window" width="430"> |

<img src="docs/images/dialog.png" alt="A themed confirmation dialog" width="380">

---

## Getting your Postman collections in

**From Postman** (toolbar) opens a dialog with two dependable routes:

- **On this computer** — scans Downloads, Documents, Desktop and OneDrive for
  `*.postman_collection.json`, `*.postman_environment.json`, globals and full data dumps, shows
  what each file contains (name, kind, request count) and imports the ones you tick. You can point
  it at extra folders too.
- **Postman account** — paste a personal API key (postman.co → Settings → API keys) and GetMan
  lists every collection and environment on the account through `api.getpostman.com`, then
  downloads the ones you pick. Since Postman 10 the desktop app is cloud-backed, so this is exactly
  what the installed app shows.

### Why GetMan does not read Postman's own database

GetMan detects a local Postman install and reports its version, but it does not read Postman's
store, because since Postman 10 there is nothing there worth reading:

- **Saved requests are not on disk.** They live in your Postman account. That is what the account
  route above fetches, and `GET /collections` covers every workspace the key can reach, so nothing
  saved is left behind.
- **The local Chromium IndexedDB holds app state**, not request bodies — which panel is open, which
  tab is active, and a cache of environment *values*. Measured on a machine with eight collections
  and roughly two hundred requests, every local store together held twelve non-Postman URLs, and
  all of them were environment values.
- **Unsaved edits in an open tab are held in memory** and never written out at all.

So a request that is open in Postman but not saved cannot be imported by anything reading the disk.
Save the tab in Postman — GetMan's account import has it a moment later, from the same cloud.
`Settings → Data → Export Data` is the other complete route: one dump with everything Postman holds,
including local and Scratch Pad collections, in a format GetMan already reads.

## What GetMan can import

**Import** — `Import` button, `Ctrl+O`, or `Paste / cURL` for raw text. The format is detected from
the content, so the same button takes all of these:

| Format | Supported |
|---|---|
| Postman collection v2.1 | yes |
| Postman collection v2.0 | yes |
| Postman collection v1 (`requests` + `folders`) | yes |
| Postman environment / globals export | yes |
| Postman "Export data" dump (`collections` + `environments` + `globals`) | yes |
| **OpenAPI 3.0 / 3.1**, JSON or YAML | yes |
| **Swagger 2.0**, JSON or YAML | yes |
| cURL command (bash or cmd quoting) | yes |

### OpenAPI and Swagger

Point GetMan at a `swagger.json` or an `openapi.yaml` and you get a working collection, not a list
of URLs:

- **One folder per tag.** An operation with no tag goes under its first path segment.
- **`servers` becomes `{{baseUrl}}`**, as a collection variable *and* an environment, so switching
  between staging and production is a dropdown. A templated server such as
  `https://{region}.api.example.com` turns `region` into its own variable, seeded with the declared
  default.
- **Parameters become rows.** Required query parameters go into the URL; optional ones are disabled
  rows you tick when you need them. `path` parameters become `:name` segments, `header` parameters
  become headers, and every one keeps its description.
- **Request bodies are generated from the schema** — `$ref` followed, `allOf` merged, `oneOf`/`anyOf`
  taking the first branch, `example` and `default` and `enum` preferred over placeholders, and
  formats turned into plausible values (`date` → `2026-01-31`, `uuid` → `{{$guid}}`,
  `email` → `user@example.com`). A schema that refers to itself terminates instead of looping.
  `multipart/form-data` becomes form-data with `format: binary` fields as file rows;
  `application/x-www-form-urlencoded` becomes a urlencoded body.
- **Security schemes become auth.** `http bearer`, `http basic`, `apiKey` (header or query) and
  `oauth2` map onto GetMan's auth, with the credentials left as empty collection variables to fill
  in — a description never contains a secret, and GetMan does not invent one. A requirement declared
  on one operation overrides the collection default.

Anything GetMan could not carry across is reported after the import rather than dropped silently:
a second server, a body media type it does not build, a security scheme with no equivalent.

**Not imported:** WSDL, HAR, Insomnia. Those are separate formats.

### Postman collections

Everything inside the collection comes across: nested folders, query params (including disabled
rows), headers as arrays *or* as a newline string, all body modes (`raw` with its language,
`urlencoded`, `formdata` with file fields, `file`, `graphql`), collection/folder/request auth,
pre-request and test scripts (`exec` as an array or a single string), collection and folder
variables, path variables, description objects, and `protocolProfileBehavior`
(`followRedirects`, `strictSSL`, `maxRedirects`, `disableUrlEncoding`, `disableCookies`).

**Export** — right-click a collection → *Export as Postman v2.1*, or export any environment.
Exports re-import cleanly into Postman and into GetMan, including a collection that started life as
an OpenAPI description.

---

## What is in the box

**Request builder**
- Every HTTP method plus custom ones (`PURGE`, `PROPFIND`, …)
- URL bar two-way synced with the query-param table; `:pathVariable` segments become editable rows
- Header table with per-row enable/disable and descriptions
- Bodies: none, form-data (with file uploads), x-www-form-urlencoded, raw
  (JSON/XML/HTML/JavaScript/text with beautify), binary file, GraphQL (query + variables)
- Per-request settings: redirects and max redirects, SSL verification, URL encoding, cookie
  send/store, timeout, HTTP version (1.0/1.1/2/3)

**Authorization** — Inherit, None, Bearer, Basic, API key (header or query), OAuth 2.0
(client credentials, authorization code with PKCE and a local redirect listener, password,
refresh token), Digest (challenge/response with MD5, MD5-sess, SHA-256), NTLM, AWS Signature v4,
and Hawk. Auth set on a collection or folder is inherited downward exactly like Postman.

**Scripting** — a real JavaScript sandbox (Jint) with the Postman API:
`pm.test`, `pm.expect` (a chai-style assertion library: `equal`, `eql`, `a`/`an`, `above`/`below`/
`least`/`most`/`within`, `include`, `property`, `lengthOf`, `keys`, `members`, `match`, `oneOf`,
`empty`, `ok`, `true`/`false`/`null`/`undefined`, and `.not` on all of them), `pm.response.to.have
.status/header/jsonBody/body`, `pm.response.to.be.json/ok/success/clientError/serverError`,
`pm.environment` / `pm.globals` / `pm.collectionVariables` / `pm.variables` / `pm.iterationData`,
`pm.request` mutation (`headers.add/upsert/remove`, method, url, body), `pm.sendRequest`,
`pm.cookies`, `pm.info`, `pm.execution.setNextRequest`, `console.*`, plus the legacy
`postman.setEnvironmentVariable`, `tests["name"] = …`, `responseCode`, `responseBody`, `xml2Json`,
`btoa`/`atob`. Collection → folder → request scripts run in that order.

**Variables** — global, collection, folder, environment, data and script-local scopes with
Postman's precedence, `{{nested}}` resolution, and the dynamic generators (`{{$guid}}`,
`{{$timestamp}}`, `{{$isoTimestamp}}`, `{{$randomInt}}` — including `{{$randomInt(1,100)}}` —
`{{$randomFullName}}`, `{{$randomEmail}}`, and ~40 more). Unresolved tokens are left intact
rather than blanked, so you can see what is missing.

**Response viewer** — Pretty / Raw / Preview, syntax highlighting for JSON, XML/HTML and
JavaScript, image rendering, `Ctrl+F` search, response headers, cookies, test results, a console
pane, and a timing breakdown (DNS, TCP, TLS, time-to-first-byte, download, total).

**Collection runner** — pick requests, set iterations and delay, drive it from a CSV or JSON data
file, stop on failure, honour `setNextRequest`, and watch per-request test results live.

**Secrets encrypted at rest** — passwords, tokens and keys are sealed with Windows DPAPI before
they touch the disk, and exports stay plain so they still open in Postman.

**Command line** — the same runner without the window, for CI. See
[Running collections from the command line](#running-collections-from-the-command-line).

**Interface languages** — English, Russian and Uzbek, switched live from the app bar. See
[Interface languages](#interface-languages).

**Also** — a shared cookie jar with a manager, request history, code generation for 15 targets
(cURL, PowerShell, C#, Python, JavaScript fetch/axios, Node, Go, Java, PHP, Ruby, Rust, Dart, raw
HTTP), tabbed requests with dirty markers, drag-and-drop reordering of the tree, system or custom
proxy, and client certificates.

**Shortcuts** — `Ctrl+Enter` send · `Ctrl+S` save · `Ctrl+N` new request · `Ctrl+W` close tab ·
`Ctrl+O` import · `Ctrl+E` environments · `Ctrl+R` runner · `Ctrl+Shift+D` light/dark · `F2` rename
the selected request, folder or collection.

## Running collections from the command line

`getman` is a second, console-only executable that drives the **same** engine as the window —
the same importer, variable resolver, auth signing and Jint script runtime. A collection that
passes in the app passes here, and the other way round. It is the piece you point a CI job at.

It runs on **Linux, macOS and Windows** (x64 and arm64; Windows x64 only). Download the build for
your platform, make it executable and it needs nothing else installed:

```bash
curl -sSLo getman https://github.com/SharofSoliyev/getman/releases/latest/download/getman-1.1.0-linux-x64
chmod +x getman
./getman --version
```

```
getman run api.postman_collection.json -e staging.postman_environment.json
getman run api.json -d users.csv -n 50 --delay 200 --bail
getman run api.json -r junit -o results/getman.xml
```

```
  GetMan 1.0.0 - running "CLI demo"

  ✓  GET    Echo GET   200 OK   630 ms   1.1 KB
       ✓ Status code is 200
       ✓ Echoes the who variable
  ✗  GET    Deliberate failure   404 Not Found   185 ms   416 B
       ✗ This one is meant to fail  expected response code to be 200 but got 404

  3 request(s), 5 assertion(s), 4 passed, 1 failed
  total 1.16 s
```

| Option | What it does |
|---|---|
| `-e, --environment <file>` | Postman environment export; repeat to merge, left to right |
| `-g, --globals <file>` | Postman globals export |
| `-d, --data <file>` | CSV or JSON data file, one iteration per row |
| `-n, --iterations <n>` | iteration count (default: the data row count, else 1) |
| `--delay <ms>` | wait between requests |
| `--folder <name>` | run only this folder of the collection |
| `--var name=value` | set a variable; wins over the environment file, repeatable |
| `--timeout <ms>` / `--script-timeout <ms>` | per-request and per-script timeouts |
| `--insecure` | do not verify TLS certificates |
| `--bail` | stop at the first failing request |
| `-r, --reporter <cli\|json\|junit>` | output format |
| `-o, --output <file>` | write the report to a file instead of stdout |
| `--lang <en\|ru\|uz>` · `--no-color` | language and plain output |

**Exit codes** — `0` everything answered and every assertion passed · `1` an assertion failed or a
request never got a response · `2` the arguments or the files were wrong. That is all a CI job
needs; `--reporter junit` on top gives it a test report to render.

Variables a script sets carry to the next request exactly as they do in the app, so a login request
that stores a token and a later request that spends it work unchanged.

```yaml
- run: getman run api.json -e ci.postman_environment.json --var token=${{ secrets.API_TOKEN }} -r junit -o report.xml
```

## Design system

The interface follows a **Developer Tool / IDE** design system on top of Fluent 2
(WPF-UI, MIT), with a Mica backdrop on Windows 11.

**Colour.** A slate canvas with two accents that never do each other's job — green is the run
action, sky is selection and focus. Method and status colours stay semantic on top of that.

| Token | Dark | Light | Used for |
|---|---|---|---|
| `Bg0` … `Bg4` | `#0F172A` → `#334155` | `#FFFFFF` → `#DCE5EF` | canvas, panel, chrome, hover, selected |
| `Fg` / `FgDim` / `FgMuted` | `#F8FAFC` / `#94A3B8` / `#64748B` | `#0F172A` / `#475569` / `#64748B` | text ladder |
| `Action` | `#22C55E` | `#16A34A` | Send and every primary button |
| `Accent` | `#38BDF8` | `#0284C7` | selection, tab underline, focus ring, links |

Contrast against the canvas: `Fg` 16.4:1, `FgDim` 7.4:1, `FgMuted` 4.6:1 in dark; 17.9:1 / 7.5:1 /
4.8:1 in light — all above the 4.5:1 floor.

**Type.** Fira Sans for the interface and Fira Mono for anything code shaped (URLs, headers, bodies,
scripts, snippets). Both are embedded in the binary under the SIL Open Font License, and both cover
Latin, Cyrillic and Greek.

**Layout.** A 52px app bar, a 72px icon navigation rail (Collections / Environments / History, with
runner, cookies and settings pinned to the foot), a resizable sidebar, and the request-over-response
split. Spacing runs on a 4/8/12/16/24 scale, radii on 5/8/12.

**Themes.** Dark by default, light one click away (toolbar, or `Ctrl+Shift+D`). Every colour is a
`DynamicResource` token, so `Themes/Tokens.Dark.xaml` and `Themes/Tokens.Light.xaml` swap at run
time — including the editor's syntax highlighting, which has its own light palette.

**Charts.** The timing breakdown is a stacked waterfall bar with a legend, plus the same numbers as
a table underneath: a pie would be harder to read at these proportions and worse for screen readers.

**Accessibility.** Visible sky focus rings on every interactive control, `AutomationProperties.Name`
on icon-only buttons, empty states that say what to do next, errors reported with an icon and text
rather than colour alone, and Windows' "show animations" preference honoured — when it is off every
duration collapses to zero.

**Motion.** Everything interactive responds to the pointer:

| Element | Hover | Selected / pressed |
|---|---|---|
| Buttons | scale up, border warms to the accent | scale down on press, primary button lifts a step of elevation |
| Icon buttons | scale up, tint to the accent | scale down on press |
| Send button | the paper plane nudges forward | — |
| Tree rows / list rows | wash fades in, content slides right | accent bar pops in from the left |
| Section tabs | wash fades in, label brightens | underline grows out from the centre |
| Request tabs | wash fades in, close button fades from faint to solid | accent bar grows along the top |
| Tree expander | chevron tints | rotates 90° |
| Fields | soft glow fades in | brighter glow while focused |
| Cards | lift 2px, elevation Dp1 → Dp3 | — |
| Splitters | accent fades over the divider | stays lit while dragging |
| Response pane | — | rises 14px and fades in each time a response lands |

Timings are 130–280ms with a cubic ease-out, short enough to feel instant, and they live in the
`DurFast` / `DurSlow` / `DurPop` resources so reduced-motion can zero them. Two mechanisms carry the
effects: `Controls/HoverAssist.cs` (an attached property giving any element scale / lift / slide /
rotate, with per-element transforms built in code) and layered `Opacity` overlays inside the
templates GetMan owns, so hover and selection never fight over the same brush. Nothing animates a
brush — brushes handed to a `Setter` get frozen by style sealing and would throw at run time.

`Themes/Tokens.*.xaml` hold the colour tokens, `Themes/Typography.xaml` the fonts and scale,
`Themes/Animations.xaml` the motion, and `Themes/Controls.xaml` is a thin layer over the Material
styles — change those to re-skin the app.

---

## Layout

```
src/GetMan/
  Models/        request, collection, environment and response models
  Services/      HTTP engine, variable resolver, script runtime, Postman and OpenAPI import,
                 Postman export, cURL import, code generation, persistence
  ViewModels/    MainViewModel, RequestTabViewModel
  Views/         request/response editors and the dialog windows
  Controls/      AvalonEdit host, key-value grid, converters
  Themes/        dark colour and control styles
src/GetMan.Cli/   console runner: argument parsing, reporters (cli, json, junit)
tools/SelfTest/  headless test suite over the whole service layer
tools/fixtures/  collections the tests and CI run against
```

`src/GetMan.Cli` shares `Models/` and `Services/` by source rather than by a project reference:
`GetMan.csproj` is a WPF `WinExe`, and referencing it would pull the entire interface — and a second
`Main` — into a console tool. That only works because the model and service layers have no WPF
dependency, which the CLI build now enforces.

## Tests

```
dotnet run --project tools/SelfTest                      # includes live HTTP against postman-echo
dotnet run --project tools/SelfTest -- --offline         # skip the network section
dotnet run --project tools/SelfTest -- --import a.json   # import real collections and round-trip them
dotnet run --project tools/SelfTest -- --unicode         # Cyrillic / CJK / emoji round-trip only

GetMan.exe --self-check                                  # builds every window, then drives the real
                                                         # interaction flows (rename focus, create,
                                                         # search, drag, tabs, theme) in a sandbox
GetMan.exe --render auth shot.png [light]                # render one view off-screen for design review
GetMan.exe --shots docs/images                           # regenerate every documentation screenshot

getman run tools/fixtures/offline-smoke.postman_collection.json -r junit -o cli.xml
                                                         # end-to-end CLI check; the fixture points
                                                         # at a closed port, so it needs no network
                                                         # and must exit 1
powershell -File tools/capture.ps1 -Out shot.png         # screenshot the running app
powershell -File tools/capture.ps1 -HoverX 115 -HoverY 244  # ...with the pointer parked to show hover
```

The suite covers collection import (v1/v2.0/v2.1 plus awkward real-world shapes), environment
import, export round-tripping, local Postman discovery, variable precedence and dynamic variables,
cURL parsing, request preparation and auth inheritance, AWS SigV4 / Digest / Hawk signing, code
generation, the whole `pm.*` script surface, live HTTP
(GET/POST/form/multipart/basic-auth/cookies/404/DNS failure), and an end-to-end run where a
collection script, a request script, the request itself and its tests all have to agree.

---

## Contributing

Bug reports, translations and pull requests are welcome. Start with
[CONTRIBUTING.md](CONTRIBUTING.md) — it covers the build, the two test suites every change has to
pass, and how to add or correct a language. Everyone taking part is expected to follow the
[Code of Conduct](CODE_OF_CONDUCT.md). Security issues have their own route:
[SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE) © GetMan contributors.

GetMan stands on other people's work:
[WPF-UI](https://github.com/lepoco/wpfui) (MIT), [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) (MIT),
[Jint](https://github.com/sebastienros/jint) (BSD-2-Clause),
[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) (MIT),
[YamlDotNet](https://github.com/aaubry/YamlDotNet) (MIT) and
[Fira Sans / Fira Mono](https://github.com/mozilla/Fira) (SIL Open Font License 1.1).

GetMan is not affiliated with Postman, Inc. "Postman" is used only to describe the file formats and
the API that GetMan reads.
