# Architecture

This is the long-form companion to [`../README.md`](../README.md). It exists to record
*why* the code looks the way it does, so that a future reader (or a future me) can
tell when a "simplification" would actually break a load-bearing invariant.

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

The reducer is invoked on a single dedicated loop (`AlarmStoreHostedService` →
`AlarmStore.RunAsync`). There is no caller-side locking, no `SemaphoreSlim`, no
manual state machine. Effects are *values*, not method calls; they are placed on a
separate channel that the `EffectInterpreterHostedService` consumes. When an effect
completes, the interpreter dispatches a follow-up event back into the event channel,
closing the loop.

This is Redux/Elm with a `Channel<T>` instead of a JS dispatcher. The reason for the
extra effect channel is that it keeps each reducer turn synchronous from the
reducer's point of view, while making side effects (which can take seconds — audio
playback, volume capture) non-blocking.

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
C# compiler enforces this when you `switch` over `AlarmState`.

### The load-bearing detail: `Ringing` owns its `RestorePoint`

The original implementation kept the captured volume in a nullable field on the
"ringing coordinator" class. The result was that *every code path that left the
Ringing state had to remember to restore-and-clear that field*, and at least two
paths existed where an exception during start-up could leave the field populated
without the volume actually being maxed (or vice versa).

The fix is structural, not procedural. `Ringing` carries the `VolumeSnapshot` as a
required record field. The reducer cannot construct `Ringing` without it. Every
transition that exits `Ringing` necessarily produces an `EndRinging(snapshot)`
effect, which restores precisely that captured value. "We forgot to restore" is no
longer expressible.

## Effects as composite atomic units

```csharp
public abstract record AlarmEffect
{
    public sealed record BeginRinging(AlarmSchedule Schedule) : AlarmEffect;
    public sealed record EndRinging(VolumeSnapshot RestorePoint) : AlarmEffect;
    public sealed record NotifyError(AlarmError Error) : AlarmEffect;
}
```

A first instinct is to break `BeginRinging` into `CaptureVolume`, `SetVolumeMax`,
`StartPlayback`. Don't. The atomicity matters: if a `Stop` request arrives between
`CaptureVolume` and `StartPlayback`, you have a captured snapshot with nothing to
restore *from*. By keeping the sequence inside one interpreter method, the
intermediate states are never visible to the rest of the system. Either the whole
`BeginRinging` succeeds and produces `AlarmEvent.RingingBegan(snapshot)`, or it
fails and produces `AlarmEvent.EffectFailed(...)` and the reducer routes the
recovery itself.

The same logic applies to `EndRinging`: cancel the playback token, await its
completion, restore the snapshot, dispatch `RingingEnded`. One method, one
responsibility.

## The reducer is pure (and that matters for tests)

`AlarmReducer.Reduce` takes `(AlarmState, AlarmEvent, DateTimeOffset)` and returns
`(AlarmState, ImmutableArray<AlarmEffect>)`. It does not take an `IClock`, an
`ILogger`, or any other service. The time is an argument because Elm taught us that
"current time" is an effect — at the *caller*, the store, you pull it from
`TimeProvider`, but at the reducer it's just a parameter.

This means the reducer's tests are tables, not scenarios:

```csharp
[Theory]
[InlineData(typeof(AlarmEvent.CancelRequested))]
[InlineData(typeof(AlarmEvent.StopRingingRequested))]
[InlineData(typeof(AlarmEvent.Tick))]
[InlineData(typeof(AlarmEvent.RingingBegan))]
[InlineData(typeof(AlarmEvent.RingingEnded))]
public void Idle_IgnoresEverythingExceptArm(Type eventType) { /* ... */ }
```

Every state × event pair has a documented row. If you add a state or an event,
unrelated tests stay green but the table loses coverage — review noticed, not
forgotten. `AlarmStoreScenarioTests` then exercises the full Store+Interpreter
pipeline with fake ports and a `FakeTimeProvider`, but only for a small set of
golden-path scenarios; the reducer table carries the bulk of the correctness load.

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

The store exposes its state as `R3.Observable<AlarmState>`. There are exactly two
subscribers — `MainViewModel` (which projects the state into bindable properties)
and `TrayStatusPresenter` (which derives a tooltip string). Neither knows about the
other. Adding a third subscriber (e.g. a notification, a log sink) is a one-line
change at the composition root.

We did *not* adopt `System.Reactive`. R3 is closer to `TimeProvider`-native and has
a cleaner sync-context story for WinUI's `DispatcherQueue`. We use about five R3
operators total (`Subscribe`, `DistinctUntilChanged`, `Select`, `Interval`,
`BehaviorSubject`), so the dependency is small in surface area.

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

| Library | Why we skipped it |
|---|---|
| **MediatR** | Adds a layer of dispatcher indirection for what is a 6-event state machine. The reducer is already the dispatcher. v12+ has a paid commercial license; that's enough reason to avoid it in a personal project on its own. |
| **OneOf / FluentResults** | `Result<TOk, TErr>` is 40 lines of code in `Alarm.Domain/Common/Result.cs`. Two operations (`Map`, `Bind`) plus a non-generic `Result.Ok<T,E>()` / `Result.Err<T,E>()` factory (the non-generic helper exists so CA1000 stays clean). Adding a third-party dependency for this would obscure the design. |
| **Stateless** | A state-machine builder DSL that wants to embed effects in `OnEntry`/`OnExit` callbacks. That is the opposite of what we want — effects are values, not callbacks; the state machine is a switch expression, not an OOP graph. |
| **AutoMapper** | The largest projection in this codebase is `AlarmState → MainViewModel` properties (a 12-line switch expression). |
| **Microsoft.Xaml.Interactivity.WinUI.Managed** (raw) | We *do* depend on it transitively via `CommunityToolkit.WinUI.Behaviors`, which is the right level of abstraction for `Behavior<T>`. |

## Things that aren't done yet

- **Persistence.** `AlarmPreferences` (the time and sound selection) is not saved
  across restarts. The plan in `docs/ARCHITECTURE.md` (this file) intentionally
  scopes that out — the reducer is prepared for a `PreferencesLoaded` event but no
  port implements it. Adding it is one infrastructure class + one DI registration
  + a `PreferencesLoaded` arm in the reducer.
- **Carryover for an armed alarm across restart.** Same reason. If the alarm was
  armed when the app exited, the next launch starts in `Idle`.
- **Localization.** All user-facing strings are English literals in
  `TrayStatusPresenter` and the XAML. There is no resource lookup yet.

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
- `tests/Alarm.Application.Tests/Reducer/AlarmReducerTests.cs` — the contract
