using System.Threading.Channels;
using Alarm.Application.Effects;
using Alarm.Application.Events;
using Alarm.Application.Ports;
using Alarm.Application.State;
using Alarm.Application.Store;
using Alarm.Application.Tests.Fakes;
using Alarm.Domain.Common;
using Alarm.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using R3;
using Shouldly;
using Xunit;

namespace Alarm.Application.Tests.Store;

public class EffectInterpreterTests
{
    [Fact]
    public async Task EndBeforePlaybackReady_JoinsAndRestoresWithoutPublishingRinging()
    {
        var readiness = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = new InterpreterHarness(new FakeAudioPlayer(readiness.Task));
        await harness.SendAsync(new AlarmEffect.BeginRinging(harness.Schedule));
        await harness.SendAsync(new AlarmEffect.EndRinging(harness.Schedule, harness.Snapshot));

        await Task.WhenAny(harness.Store.RingingBeforeReady, harness.Volume.Restored).WaitAsync(TestContext.Current.CancellationToken);

        harness.Store.RingingBeforeReady.IsCompleted.ShouldBeFalse("RingingBegan requires playback readiness before publication");
        await harness.CompleteAsync();
        await harness.Player.Stopped.WaitAsync(TestContext.Current.CancellationToken);
        harness.AssertRestored();
        harness.Store.RingingCount.ShouldBe(0);

        readiness.TrySetResult();
        harness.Player.IsPlaying.ShouldBeFalse();
        harness.Store.RingingCount.ShouldBe(0);
    }

    [Fact]
    public async Task StartupFailure_RestoresWithoutPublishingRinging()
    {
        await using var harness = new InterpreterHarness(new FakeAudioPlayer(startupFailure: new InvalidOperationException("Device start failed.")));
        await harness.SendAsync(new AlarmEffect.BeginRinging(harness.Schedule));
        var failure = await harness.Store.Failure.WaitAsync(TestContext.Current.CancellationToken);
        ReferenceEquals(failure.Schedule, harness.Schedule).ShouldBeTrue();
        await harness.SendAsync(new AlarmEffect.CancelRinging(harness.Schedule));
        await harness.CompleteAsync();

        harness.AssertRestored();
        harness.Store.RingingCount.ShouldBe(0);
        harness.Store.Failures.Count.ShouldBe(1);
    }

    [Fact]
    public async Task CleanupFailureBeforeReadiness_IsReportedAfterJoiningAndRestoring()
    {
        var readiness = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new FakeAudioPlayer(readiness.Task, completionFailure: new InvalidOperationException("Playback cleanup failed."));
        await using var harness = new InterpreterHarness(player);
        await harness.SendAsync(new AlarmEffect.BeginRinging(harness.Schedule));
        await harness.SendAsync(new AlarmEffect.EndRinging(harness.Schedule, harness.Snapshot));
        await harness.CompleteAsync();

        harness.AssertRestored();
        player.Stopped.IsCompletedSuccessfully.ShouldBeTrue();
        harness.Store.RingingCount.ShouldBe(0);
        harness.Store.Failures.Count.ShouldBe(1);
        harness.Store.Failures[0].Error.ShouldBe(new AlarmError.Unexpected("Playback cleanup failed."));
    }

    [Fact]
    public async Task StartupAndCleanupFailures_PreserveBothDistinctDiagnostics()
    {
        var player = new FakeAudioPlayer(
            startupFailure: new InvalidOperationException("Device start failed."),
            completionFailure: new InvalidOperationException("Playback cleanup failed."));
        await using var harness = new InterpreterHarness(player);
        await harness.SendAsync(new AlarmEffect.BeginRinging(harness.Schedule));
        await harness.Store.Failure.WaitAsync(TestContext.Current.CancellationToken);
        await harness.SendAsync(new AlarmEffect.CancelRinging(harness.Schedule));
        await harness.CompleteAsync();

        harness.AssertRestored();
        harness.Store.RingingCount.ShouldBe(0);
        harness.Store.Failures.Count.ShouldBe(2);
        harness.Store.Failures[0].Error.ShouldBe(new AlarmError.Unexpected("Device start failed."));
        harness.Store.Failures[1].Error.ShouldBe(new AlarmError.Unexpected("Playback cleanup failed."));
    }

    [Fact]
    public async Task CaptureFailure_DoesNotStartPlaybackOrChangeVolume()
    {
        await using var harness = new InterpreterHarness(new FakeAudioPlayer(), failOnCapture: true);
        await harness.SendAsync(new AlarmEffect.BeginRinging(harness.Schedule));
        await harness.CompleteAsync();

        harness.Volume.Current.ShouldBe(harness.Snapshot);
        harness.Volume.CaptureCount.ShouldBe(1);
        harness.Volume.ApplyCount.ShouldBe(0);
        harness.Player.PlayCallCount.ShouldBe(0);
        harness.Store.RingingCount.ShouldBe(0);
        harness.Store.Failures.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SessionConstructionFailure_RestoresCapturedVolume()
    {
        await using var harness = new InterpreterHarness(new FakeAudioPlayer(throwOnStart: true));
        await harness.SendAsync(new AlarmEffect.BeginRinging(harness.Schedule));
        await harness.CompleteAsync();

        harness.AssertRestored();
        harness.Player.PlayCallCount.ShouldBe(0);
        harness.Store.RingingCount.ShouldBe(0);
        harness.Store.Failure.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyMaximumFailure_RestoresThePreviouslyCapturedVolume()
    {
        await using var harness = new InterpreterHarness(new FakeAudioPlayer(), failOnMaximum: true);
        await harness.SendAsync(new AlarmEffect.BeginRinging(harness.Schedule));
        await harness.CompleteAsync();

        harness.AssertRestored();
        harness.Player.PlayCallCount.ShouldBe(0);
        harness.Store.RingingCount.ShouldBe(0);
        harness.Store.Failure.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task RepeatedBeginAndStop_DoNotReplaceOrRestoreTheSnapshotTwice()
    {
        var readiness = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = new InterpreterHarness(new FakeAudioPlayer(readiness.Task));
        await harness.SendAsync(new AlarmEffect.BeginRinging(harness.Schedule));
        await harness.SendAsync(new AlarmEffect.BeginRinging(harness.Schedule));
        await harness.SendAsync(new AlarmEffect.EndRinging(harness.Schedule, harness.Snapshot));
        await harness.SendAsync(new AlarmEffect.EndRinging(harness.Schedule, harness.Snapshot));
        await harness.CompleteAsync();

        harness.AssertRestored();
        harness.Player.PlayCallCount.ShouldBe(1);
        harness.Store.RingingCount.ShouldBe(0);
    }

    [Fact]
    public async Task ReadyPlayback_StopsBeforeRestoringVolume()
    {
        await using var harness = new InterpreterHarness(new FakeAudioPlayer());
        await harness.SendAsync(new AlarmEffect.BeginRinging(harness.Schedule));
        await harness.Store.Ringing.WaitAsync(TestContext.Current.CancellationToken);
        harness.Player.IsPlaying.ShouldBeTrue();
        harness.Store.RingingBeforeReady.IsCompleted.ShouldBeFalse();
        await harness.SendAsync(new AlarmEffect.EndRinging(harness.Schedule, harness.Snapshot));
        await harness.CompleteAsync();

        harness.AssertRestored();
        harness.Store.RingingCount.ShouldBe(1);
    }

    [Fact]
    public async Task OuterCancellationDuringStartup_JoinsAndRestoresWithoutReadiness()
    {
        var readiness = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = new InterpreterHarness(new FakeAudioPlayer(readiness.Task));
        await harness.SendAsync(new AlarmEffect.BeginRinging(harness.Schedule));
        await harness.Player.StartRequested.WaitAsync(TestContext.Current.CancellationToken);
        await harness.CancelAsync();

        harness.AssertRestored();
        harness.Store.RingingCount.ShouldBe(0);
        readiness.TrySetResult();
        harness.Player.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public async Task RunShutdownAndDispose_JoinOneCleanupOwner()
    {
        var readiness = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new FakeAudioPlayer(readiness.Task, completionGate: completion.Task);
        await using var harness = new InterpreterHarness(player);
        await harness.SendAsync(new AlarmEffect.BeginRinging(harness.Schedule));
        await player.StartRequested.WaitAsync(TestContext.Current.CancellationToken);

        try
        {
            var shutdown = harness.CompleteAsync();
            await player.CancellationObserved.WaitAsync(TestContext.Current.CancellationToken);
            var disposal = harness.DisposeInterpreterAsync().AsTask();
            shutdown.IsCompleted.ShouldBeFalse();
            disposal.IsCompleted.ShouldBeFalse();

            completion.TrySetResult();
            await Task.WhenAll(shutdown, disposal).WaitAsync(TestContext.Current.CancellationToken);
            harness.AssertRestored();
            harness.Store.RingingCount.ShouldBe(0);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    [Fact]
    public async Task StaleStopWithEqualScheduleValues_CannotStopTheCurrentOperation()
    {
        await using var harness = new InterpreterHarness(new FakeAudioPlayer());
        await harness.SendAsync(new AlarmEffect.BeginRinging(harness.Schedule));
        await harness.Store.Ringing.WaitAsync(TestContext.Current.CancellationToken);
        var stale = AlarmSchedule.Create(
            TimeOfDay.Of(7, 0),
            AudioSource.SystemDefault.Instance,
            new DateTimeOffset(2026, 5, 27, 22, 0, 0, TimeSpan.FromHours(9)));
        stale.ShouldBe(harness.Schedule);
        ReferenceEquals(stale, harness.Schedule).ShouldBeFalse();
        await harness.SendAsync(new AlarmEffect.EndRinging(stale, harness.Snapshot));
        await harness.SendAsync(new AlarmEffect.CancelRinging(stale));
        await harness.SendAsync(new AlarmEffect.EndRinging(harness.Schedule, harness.Snapshot));
        await harness.CompleteAsync();

        harness.AssertRestored();
        harness.Store.EndedCount.ShouldBe(1);
        ReferenceEquals(harness.Store.LastEnded, harness.Schedule).ShouldBeTrue();
    }

    private sealed class InterpreterHarness : IAsyncDisposable
    {
        private readonly Channel<AlarmEffect> _effects = Channel.CreateUnbounded<AlarmEffect>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        private readonly CancellationTokenSource _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private readonly EffectInterpreter _interpreter;
        private readonly Task _loop;

        public InterpreterHarness(FakeAudioPlayer player, bool failOnMaximum = false, bool failOnCapture = false)
        {
            Player = player;
            Volume = new SignalingVolumeController(Snapshot, failOnMaximum, failOnCapture);
            Store = new RecordingStore(player);
            _interpreter = new EffectInterpreter(Store, _effects.Reader, player, Volume, TimeProvider.System, NullLogger<EffectInterpreter>.Instance);
            _loop = _interpreter.RunAsync(_cts.Token);
        }

        public FakeAudioPlayer Player { get; }
        public SignalingVolumeController Volume { get; }
        public RecordingStore Store { get; }
        public VolumeSnapshot Snapshot { get; } = VolumeSnapshot.Of(0.42f, isMuted: false);
        public AlarmSchedule Schedule { get; } = AlarmSchedule.Create(
            TimeOfDay.Of(7, 0),
            AudioSource.SystemDefault.Instance,
            new DateTimeOffset(2026, 5, 27, 22, 0, 0, TimeSpan.FromHours(9)));

        public ValueTask SendAsync(AlarmEffect effect) => _effects.Writer.WriteAsync(effect, _cts.Token);
        public ValueTask DisposeInterpreterAsync() => _interpreter.DisposeAsync();

        public async Task CompleteAsync()
        {
            _effects.Writer.TryComplete();
            await _loop.WaitAsync(TestContext.Current.CancellationToken);
        }

        public async Task CancelAsync()
        {
            await _cts.CancelAsync();
            await _loop.WaitAsync(TestContext.Current.CancellationToken);
        }

        public void AssertRestored()
        {
            Player.IsPlaying.ShouldBeFalse();
            Volume.Current.ShouldBe(Snapshot);
            Volume.CaptureCount.ShouldBe(1);
            Volume.ApplyCount.ShouldBe(2);
        }

        public async ValueTask DisposeAsync()
        {
            _effects.Writer.TryComplete();
            await _cts.CancelAsync();
            await _loop;
            await _interpreter.DisposeAsync();
            Store.Dispose();
            _cts.Dispose();
        }
    }

    private sealed class RecordingStore(IAudioPlayer player) : IAlarmStore, IDisposable
    {
        private readonly BehaviorSubject<AlarmState> _states = new(AlarmState.Idle.Instance);
        private readonly TaskCompletionSource _ringingBeforeReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _ringing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<AlarmEvent.EffectFailed> _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<AlarmEvent.EffectFailed> _failures = [];
        private int _ringingCount;
        private int _endedCount;

        public AlarmState Current => _states.Value;
        public Observable<AlarmState> States => _states;
        public Task RingingBeforeReady => _ringingBeforeReady.Task;
        public Task Ringing => _ringing.Task;
        public Task<AlarmEvent.EffectFailed> Failure => _failure.Task;
        public List<AlarmEvent.EffectFailed> Failures => _failures;
        public int RingingCount => Volatile.Read(ref _ringingCount);
        public int EndedCount => Volatile.Read(ref _endedCount);
        public AlarmSchedule? LastEnded { get; private set; }

        public ValueTask DispatchAsync(AlarmEvent evt, CancellationToken ct = default)
        {
            if (evt is AlarmEvent.RingingBegan)
            {
                Interlocked.Increment(ref _ringingCount);
                if (!player.IsPlaying)
                {
                    _ringingBeforeReady.TrySetResult();
                }
                _ringing.TrySetResult();
            }
            else if (evt is AlarmEvent.EffectFailed failure)
            {
                _failures.Add(failure);
                _failure.TrySetResult(failure);
            }
            else if (evt is AlarmEvent.RingingEnded ended)
            {
                LastEnded = ended.Schedule;
                Interlocked.Increment(ref _endedCount);
            }

            return ValueTask.CompletedTask;
        }

        public void Dispose() => _states.Dispose();
    }

    private sealed class SignalingVolumeController : ISystemVolumeController
    {
        private readonly TaskCompletionSource _restored = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly VolumeSnapshot _initial;
        private readonly bool _failOnMaximum;
        private readonly bool _failOnCapture;

        public SignalingVolumeController(VolumeSnapshot initial, bool failOnMaximum, bool failOnCapture)
        {
            _initial = initial;
            _failOnMaximum = failOnMaximum;
            _failOnCapture = failOnCapture;
            Current = initial;
        }

        public VolumeSnapshot Current { get; private set; }
        public int CaptureCount { get; private set; }
        public int ApplyCount { get; private set; }
        public Task Restored => _restored.Task;

        public VolumeSnapshot Capture()
        {
            CaptureCount++;
            return _failOnCapture ? throw new InvalidOperationException("Volume capture failed.") : Current;
        }

        public void Apply(VolumeSnapshot snapshot)
        {
            Current = snapshot;
            ApplyCount++;
            if (snapshot == _initial)
            {
                _restored.TrySetResult();
            }
            else if (_failOnMaximum && snapshot == VolumeSnapshot.Maximum)
            {
                throw new InvalidOperationException("Volume write failed after changing the endpoint.");
            }
        }
    }
}
