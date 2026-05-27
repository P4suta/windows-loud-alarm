# Alarm

A single-window Windows alarm clock built with **.NET 10 + WinUI 3**. Long-press to
arm, hold to cancel; when the alarm fires the app pins the system master volume to
100 % and loops a sound until you long-press *STOP*, at which point the captured
volume is restored.

<!-- TODO: capture a 5-second GIF of the long-press → armed → ringing → stop flow -->

## Why this exists

It's a personal alarm app that doubles as a working playground for **Store + Reducer
+ Effect (Redux-style) architecture in .NET**, with type-level guarantees for the
critical "always restore the user's system volume" invariant. The full design
rationale lives in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

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

That's the whole setup. The pinned .NET SDK version lives in `mise.toml`, the
package versions live in `Directory.Packages.props`, and analyzer/style rules live
in `.editorconfig`.

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
| `just publish` | Produce a self-contained win-x64 binary |

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

- Four-layer Clean Architecture: `Domain` (pure) → `Application` (reducer + ports) →
  `Infrastructure` (NAudio, CoreAudio) → `Presentation` (WinUI 3).
- `AlarmReducer.Reduce(state, event, now)` is the only place state transitions live.
  It's a pure function — tested exhaustively by `AlarmReducerTests`.
- `AlarmState.Ringing` carries its own `VolumeSnapshot`, which makes "we captured a
  volume but lost it before restore" *unrepresentable* at the type level.
- `AlarmEffect.BeginRinging` and `EndRinging` are deliberately composite: the
  interpreter performs capture+max+play (or stop+restore) inside a single method, so
  a Stop arriving mid-sequence can never orphan a snapshot.
- `R3.Observable<AlarmState>` is the single stream; both `MainViewModel` (projection
  into bindable properties) and `TrayStatusPresenter` (tray tooltip) subscribe to it
  independently — neither knows about the other.

Detailed walkthrough, including which libraries were *not* adopted and why, is in
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

Two settings in `Directory.Build.props` shape the whole codebase:

- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- `<AnalysisMode>All</AnalysisMode>` (every Microsoft + Meziantou analyzer at warning
  or higher)

In practice: every analyzer warning fails the build. Suppressions are not allowed in
`.csproj`; they belong in `.editorconfig` with a comment explaining why. Production
code suppressions and test-only suppressions are kept in clearly separated sections.

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

## Contributing to your own fork

If you change behaviour:

1. Add or update a Reducer table-test row before you touch the reducer.
2. Run `just check` — it must stay green (warning 0 / 52 tests / format clean).
3. Don't add NuGet packages without justification; the design rejected MediatR,
   OneOf, and Stateless on purpose. See `docs/ARCHITECTURE.md#libraries-we-said-no-to`.

If you're an AI agent, read [`CLAUDE.md`](CLAUDE.md) first — it has the rules you
must follow when modifying this repo.

## License

Personal project, no warranty. Do whatever you want with the code; if it eats your
breakfast that's on you.
