# Alarm

[![CI](https://github.com/P4suta/windows-loud-alarm/actions/workflows/ci.yml/badge.svg)](https://github.com/P4suta/windows-loud-alarm/actions/workflows/ci.yml)
[![OpenSSF Scorecard](https://api.securityscorecards.dev/projects/github.com/P4suta/windows-loud-alarm/badge)](https://scorecard.dev/viewer/?uri=github.com/P4suta/windows-loud-alarm)

A single-window Windows alarm clock built with **.NET 10 + WinUI 3**. Long-press to
arm, hold to cancel; when the alarm fires the app pins the system master volume to
100 % and loops a sound until you long-press *STOP*, at which point the captured
volume is restored.

<!-- TODO: capture a 5-second GIF of the long-press → armed → ringing → stop flow -->

Personal alarm app with a Store + Reducer + Effect (Redux-style) architecture. Design
rationale: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Quickstart

The toolchain is managed by [`mise`](https://mise.jdx.dev/) and orchestrated by
[`just`](https://github.com/casey/just). You only need `just` on PATH; mise is
called by the bootstrap recipe.

```sh
# 1. Install mise + just (skip if you already have them via your dotfiles)
curl https://mise.run | sh
mise use -g just

# 2. Set up the project (installs the pinned .NET SDK, restores packages)
just bootstrap

# 3. Run it
just run
```

The pinned .NET SDK version lives in `mise.toml`, package versions in
`Directory.Packages.props`, analyzer/style rules in `.editorconfig`.

## Recipes

`just` (no arguments) prints the full list. The ones you reach for daily:

| Command | What it does |
|---|---|
| `just build` | Compile everything (src + tests) with strict analyzers |
| `just test` | Run all 52 tests (Domain 28 + Application 24) |
| `just run` | Launch the desktop app |
| `just watch` | Hot-reload dev loop |
| `just check` | CI-equivalent: restore → build → test → format-check |
| `just full` | Full release pipeline (clean → rebuild → test → publish) + artifact size summary, ~40 s |
| `just format` | Apply auto-fixable style fixes |
| `just doctor` | Inspect mise/dotnet/just versions when things look wrong |
| `just clean` | Delete every `bin/` and `obj/` under src/, tests/, publish/ |
| `just publish` | Assemble the downloadable bundle in `publish/dist/Alarm` (launcher + `app/`) |
| `just package vX.Y.Z` | Zip `publish/dist/Alarm` + write `SHA256SUMS.txt` for a release |

## Architecture in 30 seconds

```
                          ┌───────────────────────────────────────┐
                          │            AlarmStore (state)         │
                          │  Channel<AlarmEvent> ─▶ Reducer ─▶    │
[UI] ── DispatchAsync ───▶│  (state', effects[])                  │
                          │            │                          │
                          │            ├─▶ BehaviorSubject<State> │── observed by ViewModel + Tray
                          │            └─▶ EffectInterpreter ─────│── calls IAudioPlayer / IVolume
                          └───────────────────────────────────────┘
                                       ▲                 │
                                       └── DispatchAsync(AlarmEvent.RingingBegan/Ended/...)
```

Four-layer Clean Architecture: `Domain` (pure) → `Application` (reducer + ports) →
`Infrastructure` (NAudio, CoreAudio) → `Presentation` (WinUI 3). Details in
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Testing

```sh
just test         # all 52 tests
just test-domain  # Domain only (28)
just test-app     # Application only (24)
```

- xUnit + Shouldly. No mocking framework — fakes live in
  `tests/Alarm.Application.Tests/Fakes/`.
- The Reducer table tests are the contract. Every `(state × event)` pair has a row.

## Quality gates

`Directory.Build.props` sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and
`<AnalysisMode>All</AnalysisMode>`, so every analyzer warning fails the build.
Suppressions are not allowed in `.csproj`; they belong in `.editorconfig` with a
comment, split into production and test-only sections.

## Repo layout

```
src/Alarm.Domain         Value objects, Result<T,E>, state/event/effect hierarchies
src/Alarm.Application    Reducer, Store, EffectInterpreter, BackgroundServices, Ports
src/Alarm.Infrastructure NAudio playback, CoreAudio volume, TimeProvider clock, FallbackAudioResolver
src/Alarm.Presentation   WinUI 3 window, MainViewModel, LongPressGestureBehavior, TrayStatusPresenter
tests/                   Domain + Application unit tests, fakes, scenario tests
docs/                    ARCHITECTURE.md and other deep-dive notes
.editorconfig            All style rules and per-folder analyzer overrides
mise.toml                Pinned toolchain (.NET SDK)
justfile                 Every supported dev operation
Alarm.slnx               Solution file (src + tests folders)
```

## Releases

Versioning and releases are automated from [Conventional Commits](https://www.conventionalcommits.org/)
via [release-please](https://github.com/googleapis/release-please): `feat:`/`fix:`
commits on `main` keep a Release PR open that bumps the version + `CHANGELOG.md`;
merging it (with the `release: approved` label) cuts the `vX.Y.Z` tag and a GitHub
Release with the zipped self-contained build + `SHA256SUMS.txt` and keyless Sigstore
build-provenance/SBOM attestations. Operator guide: [`docs/RELEASING.md`](docs/RELEASING.md).

**What you download.** Extract the zip and double-click `Alarm.exe`. Layout +
rationale: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) "Distribution layout". It's
an unpackaged self-contained build; if it doesn't start, install the
[Windows App Runtime 2.x](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md). Run `just check` (must stay green: warning 0
/ 52 tests / format clean), use a Conventional-Commits PR title, add a Reducer
table-test row before touching the reducer. AI agents read [`CLAUDE.md`](CLAUDE.md)
first.

## License

Personal project, no warranty.
