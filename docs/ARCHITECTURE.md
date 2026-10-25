# Architecture

This guide extends [`../README.md`](../README.md) with runtime invariants that a simple change could break.

## Single-direction dataflow

The runtime follows one reducer and effect loop:

```text
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

`AlarmStoreHostedService` drives the reducer through `AlarmStore.RunAsync` on one dedicated loop.
Callers need no locks or `SemaphoreSlim`.
The store emits effect values on a separate channel for `EffectInterpreterHostedService` to consume.
When an effect completes, the interpreter dispatches a follow-up event and closes the loop.
This design follows Redux/Elm over `Channel<T>`.
The separate effect channel keeps reducer turns synchronous while audio playback and volume capture can take seconds.

## State as a closed hierarchy

```csharp
public abstract record AlarmState
{
    public sealed record Idle : AlarmState { public static readonly Idle Instance = new(); }
    public sealed record Armed(AlarmSchedule Schedule) : AlarmState;
    public sealed record Ringing(AlarmSchedule Schedule, VolumeSnapshot RestorePoint) : AlarmState;
}
```

`AlarmState` declares `Idle`, `Armed`, and `Ringing` as nested sealed record types.
The reducer handles state and event pairs explicitly and returns `Transition.NoOp` for unmatched pairs.

### The load-bearing detail: `Ringing` owns its `RestorePoint`

The required `VolumeSnapshot RestorePoint` field prevents the reducer from constructing a `Ringing` state without a captured snapshot.
Stopping ringing emits `EndRinging` with that snapshot while the state remains `Ringing`.
An effect failure during ringing also emits `EndRinging` with the snapshot and moves the state to `Idle`.
`RingingEnded` moves a ringing state to `Idle` without requesting another restoration.

## Effects as composite atomic units

```csharp
public abstract record AlarmEffect
{
    public sealed record BeginRinging(AlarmSchedule Schedule) : AlarmEffect;
    public sealed record EndRinging(VolumeSnapshot RestorePoint) : AlarmEffect;
    public sealed record NotifyError(AlarmError Error) : AlarmEffect;
}
```

`BeginRinging` groups volume capture, volume override, and playback-task scheduling in one interpreter method.
After scheduling playback, the interpreter dispatches `AlarmEvent.RingingBegan(snapshot)`.
Volume capture or override failures dispatch `AlarmEvent.EffectFailed(...)`, and playback exceptions also dispatch a failure event.
The volume and audio operations remain separate external calls.
`EndRinging` cancels the playback token, awaits playback, and attempts to restore the snapshot.
It dispatches `RingingEnded` after success or failure.
A failure also dispatches `EffectFailed`.

## The reducer is pure (and that matters for tests)

`AlarmReducer.Reduce` takes `(AlarmState, AlarmEvent, DateTimeOffset)` and returns a `Transition` containing the next state and an `ImmutableArray<AlarmEffect>`.
The reducer has no clock, logger, or service dependencies.
The store reads `TimeProvider` at the call site and passes the current time to the reducer.
Reducer tests check every state and event pair, while `AlarmStoreScenarioTests` covers selected store and interpreter flows with fake ports and a `FakeTimeProvider`.

## Ports & the layer ban

The ports in `Alarm.Application/Ports/` define the external dependencies of `Alarm.Application`.
`IClock` wraps `TimeProvider.GetLocalNow()`.
`IClockTicks` exposes a 1 Hz `Observable<DateTimeOffset>` for the UI countdown.
`IAudioPlayer` provides `PlayUntilCancelledAsync(source, ct)`.
`ISystemVolumeController` exposes `Capture()` and `Apply(snapshot)` for volume snapshots.
`IAudioFilePicker` opens an asynchronous dialog and returns `Result<UserFile, AlarmError>`.
`IAlarmStatusPresenter` exposes `Bind(Observable<AlarmState>)` and Show and Exit events.

`Alarm.Domain` has no project references, and `Alarm.Application` references only `Alarm.Domain`.
`Alarm.Infrastructure` implements the ports.
`Alarm.Presentation` uses `IAlarmStore` and the ports for UI-driven actions.
`mise x -- just verify-layers` confirms these project reference rules.

## R3 (Reactive Extensions for .NET) as a thin layer

The store exposes state as `R3.Observable<AlarmState>`.
`MainViewModel` subscribes for bindable properties, and `TrayStatusPresenter` subscribes for its tooltip string.
Neither subscriber knows about the other.
Developers can add a third subscriber at the composition root with one line.
The project selects R3 over `System.Reactive` for its native `TimeProvider` support.
`MainViewModel` marshals subscription callbacks through WinUI's `DispatcherQueue`.
The code uses five operators: `Subscribe`, `DistinctUntilChanged`, `Select`, `Interval`, and `BehaviorSubject`.

## Threading model

The reducer loop processes events serially, so reducer invocations don't overlap.
The effect interpreter also processes effects serially in emission order.
Long-running audio effects use background tasks without blocking the interpreter loop.
The interpreter dispatches `RingingBegan` after scheduling the playback task, then returns to consume the next effect.
Playback can end through cancellation, shutdown, or failure.
`MainViewModel` captures `DispatcherQueue.GetForCurrentThread()` at construction and uses it to marshal subscription updates to the UI thread.

## Libraries we said no to

Don't add dependencies.
The project intentionally rejects MediatR, OneOf/FluentResults, Stateless, and AutoMapper.
The reducer already dispatches events.
`Alarm.Domain/Common/Result.cs` defines `Result<TOk,TErr>` in 40 lines.
Effects are values rather than callbacks.

## Distribution layout

The app ships as an unpackaged, self-contained win-x64 build without MSIX.
Users unpack a portable folder and run the app.
`WindowsAppSDKSelfContained` remains unset because WinAppSDK 2.x makes `Microsoft.UI.Xaml.dll` crash at startup.
The target must have Windows App Runtime 2.x installed.

A self-contained WinUI build contains about 329 files and has no obvious entry point.
`just publish` runs `scripts/AssembleBundle.cs` to assemble a launcher-fronted bundle:

```text
publish/dist/Alarm/
├─ Alarm.exe        ← the one file a user runs (Native AOT launcher, src/Alarm.Launcher)
├─ README.txt       ← start guide (UTF-8 BOM + CRLF for Notepad)
├─ BUILDINFO.txt    ← version / commit / date — survives the zip name being lost
└─ app/             ← the ~329-file self-contained app, isolated
   ├─ Alarm.exe     ← the real apphost
   └─ Alarm.dll, coreclr.dll, Microsoft.WinUI.dll, …
```

The .NET apphost resolves `Alarm.dll`, `*.deps.json`, and `*.runtimeconfig.json` from its own directory.
Moving it out of `app/` prevents it from running.
The root `Alarm.exe` is a separate ~1.5 MB Native AOT program in `src/Alarm.Launcher` with `PublishAot=true`.
It starts `app\Alarm.exe` with `Process.Start`, forwards arguments, and sets the working directory to `app\`.
The launcher then exits, leaving one GUI process.
Publishing the launcher with Native AOT requires the MSVC C++ toolchain, as described in the [packaging guide](README.md#package-and-release).

`AssembleBundle.cs` verifies the bundle and fails the build if the root launcher or `app\Alarm.exe` or `app\Alarm.dll` is missing.
CI signs six first-party PE files, consisting of the root launcher and five files in `app/`.
`just package` zips the bundle contents so extraction reproduces this layout.

## File pointers

The reducer's transitions live in `src/Alarm.Application/Reducer/AlarmReducer.cs`.
`src/Alarm.Application/Store/AlarmStore.cs` contains the reducer loop and state subject.
`src/Alarm.Application/Store/EffectInterpreter.cs` implements atomic begin and end ringing effects.
`src/Alarm.Application/State/AlarmState.cs` defines the three-case closed hierarchy.
`src/Alarm.Application/Events/AlarmEvent.cs` defines every event the reducer accepts.
`src/Alarm.Application/Effects/AlarmEffect.cs` defines composite atomic effects.
`src/Alarm.Application/Runtime/*HostedService.cs` contains the store, effect, and tick loops.
`src/Alarm.Infrastructure/Audio/AudioPlayer.cs` implements the `PlayUntilCancelledAsync` contract.
`src/Alarm.Infrastructure/Audio/NAudioBackend.cs` manages NAudio device lifetime.
`src/Alarm.Infrastructure/Volume/CoreAudioVolumeController.cs` caches MMDevice instances.
`src/Alarm.Presentation/Behaviors/LongPressGestureBehavior.cs` implements long-press input and InsetClip animation.
`src/Alarm.Launcher/Program.cs` starts `app\Alarm.exe` from the root launcher.
`scripts/AssembleBundle.cs` assembles and verifies the `publish/dist/Alarm` bundle.
`tests/Alarm.Application.Tests/Reducer/AlarmReducerTests.cs` defines the reducer contract.
