# Architecture

Long-form companion to [`../README.md`](../README.md). Records the load-bearing
invariants — the ones a "simplification" would silently break.

## Single-direction dataflow

The whole runtime is one loop:

```
                  ┌─────────────────────────────────────────────┐
                  │                AlarmStore                   │
[UI] ── Dispatch ─▶ Channel<AlarmEvent>                         │
                  │     │                                       │
                  │     ▼                                       │
                  │   Reducer (pure: state, event, now → ...)   │
                  │     │                                       │
                  │     ├──▶ BehaviorSubject<AlarmState>.OnNext─┼─▶ MainViewModel
                  │     │                                       └─▶ TrayStatusPresenter
                  │     └──▶ Channel<AlarmEffect>               │
                  └──────────────────┬──────────────────────────┘
                                     │
                                     ▼
                            EffectInterpreter
                            (IAudioPlayer, IVolume, …)
                                     │
                                     └── Dispatch(AlarmEvent.RingingBegan / Ended / Failed)
```

The reducer runs on a single dedicated loop (`AlarmStoreHostedService` →
`AlarmStore.RunAsync`): no caller-side locking, no `SemaphoreSlim`. Effects are
*values* on a separate channel that `EffectInterpreterHostedService` consumes; when
an effect completes the interpreter dispatches a follow-up event, closing the loop.
This is Redux/Elm over `Channel<T>`. The separate effect channel keeps each reducer
turn synchronous while side effects (audio playback, volume capture — seconds long)
stay non-blocking.

## State as a closed hierarchy

```csharp
public abstract record AlarmState
{
    public sealed record Idle : AlarmState { public static readonly Idle Instance = new(); }
    public sealed record Armed(AlarmSchedule Schedule) : AlarmState;
    public sealed record Ringing(AlarmSchedule Schedule, VolumeSnapshot RestorePoint) : AlarmState;
}
```

Three states, exhaustively covered by the reducer's switch expression. `AlarmState`'s
constructor is private, so the only inhabitants are the three sealed records — the
compiler enforces this when you `switch` over `AlarmState`.

### The load-bearing detail: `Ringing` owns its `RestorePoint`

`Ringing` carries the `VolumeSnapshot` as a required record field, so the reducer
cannot construct `Ringing` without it. Every transition that exits `Ringing`
necessarily produces an `EndRinging(snapshot)` effect that restores exactly that
value. "Captured volume but forgot to restore" is not expressible in the type.

## Effects as composite atomic units

```csharp
public abstract record AlarmEffect
{
    public sealed record BeginRinging(AlarmSchedule Schedule) : AlarmEffect;
    public sealed record EndRinging(VolumeSnapshot RestorePoint) : AlarmEffect;
    public sealed record NotifyError(AlarmError Error) : AlarmEffect;
}
```

`BeginRinging` is composite and atomic: capture volume, set max, start playback all
live in one interpreter method, so intermediate states (a captured snapshot with
nothing playing) are never visible. Either it succeeds and produces
`AlarmEvent.RingingBegan(snapshot)`, or it fails and produces
`AlarmEvent.EffectFailed(...)` and the reducer routes recovery. `EndRinging` is the
symmetric unit: cancel the playback token, await it, restore the snapshot, dispatch
`RingingEnded`.

## The reducer is pure (and that matters for tests)

`AlarmReducer.Reduce` takes `(AlarmState, AlarmEvent, DateTimeOffset)` and returns
`(AlarmState, ImmutableArray<AlarmEffect>)` — no `IClock`, no `ILogger`, no services.
Time is a parameter; the store pulls it from `TimeProvider` at the call site. So the
reducer's tests are exhaustive tables (every state × event pair has a row);
`AlarmStoreScenarioTests` covers a few golden-path Store+Interpreter scenarios with
fake ports and a `FakeTimeProvider`.

## Ports & the layer ban

`Alarm.Application/Ports/` holds every dependency the application has on the
outside world:

- `IClock` — wraps `TimeProvider.GetLocalNow()`
- `IClockTicks` — 1 Hz `Observable<DateTimeOffset>` for UI countdown display
- `IAudioPlayer` — `PlayUntilCancelledAsync(source, ct)`
- `ISystemVolumeController` — `Capture()` / `Apply(snapshot)`
- `IAudioFilePicker` — async dialog returning `Result<UserFile, AlarmError>`
- `IAlarmStatusPresenter` — `Bind(Observable<AlarmState>)` plus Show/Exit events

`Alarm.Domain` has zero references. `Alarm.Application` references only `Alarm.Domain`.
`Alarm.Infrastructure` implements the ports. `Alarm.Presentation` consumes the store
via `IAlarmStore` and the ports for UI-driven actions. `just verify-layers` confirms
these reference rules.

## R3 (Reactive Extensions for .NET) as a thin layer

The store exposes state as `R3.Observable<AlarmState>`. Two subscribers —
`MainViewModel` (bindable properties) and `TrayStatusPresenter` (tooltip string) —
neither aware of the other; a third is a one-line change at the composition root.
R3 over `System.Reactive`: `TimeProvider`-native and a cleaner sync-context story
for WinUI's `DispatcherQueue`. About five operators total (`Subscribe`,
`DistinctUntilChanged`, `Select`, `Interval`, `BehaviorSubject`).

## Threading model

- The reducer loop is single-threaded. Race conditions inside the state machine
  cannot happen.
- The effect interpreter loop is also single-threaded. Effects execute serially in
  the order they were emitted.
- Long-running effects (audio playback) spawn background tasks but do *not* block
  the interpreter loop — the interpreter dispatches `RingingBegan` once the
  playback task has started, then returns to consume the next effect. The playback
  task itself completes (via cancellation token) only when an `EndRinging` effect
  cancels it.
- All R3 subscriptions in `MainViewModel` use `DispatcherQueue.GetForCurrentThread()`
  captured at construction to marshal updates to the UI thread.

## Libraries we said no to

Don't add dependencies. MediatR, OneOf/FluentResults, Stateless, and AutoMapper were
all intentionally rejected — the reducer is already the dispatcher, `Result<TOk,TErr>`
is 40 lines in `Alarm.Domain/Common/Result.cs`, and effects are values, not callbacks.

## Distribution layout

The app ships as an **unpackaged, self-contained** win-x64 build (no MSIX): a portable
folder the user extracts and runs. `WindowsAppSDKSelfContained` is deliberately *not*
set — under WinAppSDK 2.x it makes `Microsoft.UI.Xaml.dll` crash at startup — so the
build relies on the Windows App Runtime 2.x being installed on the target.

A self-contained WinUI build is ~329 files with no obvious entry point, so `just publish`
(`scripts/AssembleBundle.cs`) assembles a **launcher-fronted bundle**:

```
publish/dist/Alarm/
├─ Alarm.exe        ← the one file a user runs (Native AOT launcher, src/Alarm.Launcher)
├─ README.txt       ← start guide (UTF-8 BOM + CRLF for Notepad)
├─ BUILDINFO.txt    ← version / commit / date — survives the zip name being lost
└─ app/             ← the ~329-file self-contained app, isolated
   ├─ Alarm.exe     ← the real apphost
   └─ Alarm.dll, coreclr.dll, Microsoft.WinUI.dll, …
```

The .NET apphost resolves `Alarm.dll` / `*.deps.json` / `*.runtimeconfig.json` from
its own directory, so it can't be lifted out of `app/`. The root `Alarm.exe` is a
separate ~1.5 MB Native AOT program (`src/Alarm.Launcher`, `PublishAot=true`) whose
only job is to `Process.Start(app\Alarm.exe)` — forwarding args, setting the working
dir to `app\` — then exit, leaving exactly one GUI process. Native AOT costs an MSVC
C++ toolchain at publish time (see `CLAUDE.md` → Toolchain).

`AssembleBundle.cs` **self-verifies**: a missing root launcher or `app\Alarm.exe` /
`app\Alarm.dll` fails the build at the producer. CI signs six first-party PEs (root
launcher + five in `app/`); `just package` zips the bundle *contents* so extraction
reproduces this layout.

## File pointers

Critical files, with what they're responsible for:

- `src/Alarm.Application/Reducer/AlarmReducer.cs` — every state transition
- `src/Alarm.Application/Store/AlarmStore.cs` — reducer loop, state subject
- `src/Alarm.Application/Store/EffectInterpreter.cs` — atomic begin/end ringing
- `src/Alarm.Application/State/AlarmState.cs` — three-case closed hierarchy
- `src/Alarm.Application/Events/AlarmEvent.cs` — every event the reducer accepts
- `src/Alarm.Application/Effects/AlarmEffect.cs` — composite atomic effects
- `src/Alarm.Application/Runtime/*HostedService.cs` — three loops (store, effects, tick)
- `src/Alarm.Infrastructure/Audio/AudioPlayer.cs` — `PlayUntilCancelledAsync` contract
- `src/Alarm.Infrastructure/Audio/NAudioBackend.cs` — NAudio device lifetime
- `src/Alarm.Infrastructure/Volume/CoreAudioVolumeController.cs` — MMDevice cache
- `src/Alarm.Presentation/Behaviors/LongPressGestureBehavior.cs` — long-press input + InsetClip animation
- `src/Alarm.Launcher/Program.cs` — the root launcher that starts `app\Alarm.exe`
- `scripts/AssembleBundle.cs` — assembles + self-verifies the `publish/dist/Alarm` bundle
- `tests/Alarm.Application.Tests/Reducer/AlarmReducerTests.cs` — the contract
