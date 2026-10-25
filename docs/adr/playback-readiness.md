---
date: 2026-10-25
---

# Playback readiness before ringing

## Status

proposed

## Context

`RingingBegan` acknowledges accepted playback and carries the captured volume needed for restoration.
Queueing a delegate with [`Task.Run`](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.run?view=net-10.0) doesn't establish that the delegate has started.
Publishing the event after queueing permits `Ringing` before the player starts.
Waiting for startup inside the sole effect consumer would instead block a queued stop operation.

The existing state model distinguishes `Idle`, `Armed`, and `Ringing`.
`CancelRequested` disarms from `Armed`, while `StopRingingRequested` has no effect in that state.
Playback startup must preserve those command meanings and keep cancellation available before readiness.

## Decision

Return a [playback session](../../src/Alarm.Application/Ports/PlaybackSession.cs) with distinct readiness and completion tasks from the audio port.
The real adapter acknowledges readiness only after `NAudioBackend.Play` succeeds.
Completion includes every owned startup task and device cleanup.
Cancellation before startup must settle both tasks.

The [interpreter](../../src/Alarm.Application/Store/EffectInterpreter.cs) owns the original volume snapshot before applying `VolumeSnapshot.Maximum`.
One operation owns its session, cancellation source, observer, and restoration attempt.
A readiness observer runs outside the effect consumer and publishes `RingingBegan` after readiness.
The consumer cancels and joins that operation before restoring its snapshot.
Disposal signals cancellation and joins the run task, whose shutdown path remains the sole cleanup owner.
The operation creates and disposes its linked cancellation source within that ownership boundary.
`DisposeAsync` joins the run task, so operation disposal precedes completion of asynchronous disposal.
The file-scoped `CA2213` exception in [`.editorconfig`](../../.editorconfig) covers [disposal beyond the analyzer's inspected call depth](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2213#when-to-suppress-warnings).
Duplicate begin or stop effects can't replace the snapshot or restore it twice.
Startup and session-construction failures follow the same cleanup boundary.

Use the originating immutable schedule object to bind readiness, failure, and end notifications to their current cycle.
The [reducer](../../src/Alarm.Application/Reducer/AlarmReducer.cs) compares object identity, because separate arm operations may have equal schedule values.
The production factory creates a new schedule object for each arm operation.
An old notification can't cancel or end a newer cycle.
Armed cancellation requests owned startup cleanup, and matching failure preserves the existing state result while requesting cleanup.
Armed stop remains a no-op.

The interpreter runs through a .NET 10 `BackgroundService`, which starts [`ExecuteAsync` on a background thread](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task).
The audio adapter runs its session worker on the thread pool, and neither the session nor the interpreter requires UI-thread access to complete.
The app awaits host shutdown asynchronously.
A [foreign-task await](https://microsoft.github.io/vs-threading/analyzers/VSTHRD003.html) can deadlock when the task needs the main thread and its caller blocks that thread.
`VSTHRD003` analyzes task creation within each method and doesn't track ownership across startup, observation, and joining.
The interpreter's file-scoped `VSTHRD003` exception covers the owned session, observer, and run-task joins without changing task or cancellation semantics.
Both exceptions stay in `.editorconfig`, as required by the repository PR template, and apply to no other source file.
Reassess those exceptions if a future adapter requires the UI thread or a host introduces synchronous task waits.

## Consequences

The public state variants and the existing full-cycle assertions remain intact.
The audio port changes from one lifetime task to an explicit session, so each adapter must honor both completion paths.
Removing only `Task.Run` would depend on synchronous startup in every adapter and would leave asynchronous startup unspecified.
Adding sleeps or polling would leave the missing ordering relationship unchanged.
A new public startup-state hierarchy adds no required command behavior for the current single-cycle app.

The [interpreter tests](../../tests/Alarm.Application.Tests/Store/EffectInterpreterTests.cs) hold readiness behind a controlled gate and observe actual event publication.
A queued stop must join and restore without publishing readiness first.
The [scenario tests](../../tests/Alarm.Application.Tests/Store/AlarmStoreScenarioTests.cs) exercise armed cancellation through the real store and reducer.
The [transition table](../../tests/Alarm.Application.Tests/Reducer/AlarmReducerTests.cs) covers the closed state and event variants and partitions cycle identity into matching, equal-valued distinct, and different schedules.

The .NET scheduler, cancellation machinery, and native audio device remain external semantic boundaries.
The source-bound assurance alternative combines typed session roles, the finite transition partition, and controlled tests of the production interpreter.
Finite scheduling cases don't prove every possible .NET interleaving.
Successful device initialization doesn't prove physical audibility.
An uninterruptible device call can delay joining, and a stop limit during that wait leaves verification inconclusive.
The adapter must finish cleanup when the call returns rather than claim cancellation terminated native work.
Reassess identity binding if a future adapter serializes or reconstructs schedules across a process boundary.
