using Alarm.Application.Events;
using Alarm.Application.State;
using Alarm.Application.Store;
using Alarm.Application.Tests.Fakes;
using Alarm.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using R3;
using Shouldly;
using Xunit;

namespace Alarm.Application.Tests.Store;

/// <summary>
/// End-to-end scenarios driven through the real <see cref="AlarmStore"/> + <see cref="EffectInterpreter"/>
/// loop, with fake ports standing in for hardware. Each test runs both loops in the background
/// and walks the state stream via a helper that waits for a target state.
/// </summary>
public class AlarmStoreScenarioTests
{
    private static readonly TimeOfDay AlarmTime = TimeOfDay.Of(7, 0);

    [Fact]
    public async Task FullCycle_Arm_TickDue_StopRinging_RestoresVolume()
    {
        await using var harness = await Harness.StartAsync().ConfigureAwait(true);

        var capturedSnapshot = VolumeSnapshot.Of(0.42f, isMuted: false);
        harness.Volume.CurrentSystemVolume = capturedSnapshot;

        // Arm at 22:00, alarm set for 07:00 — schedule resolves to next-day 07:00.
        var now = new DateTimeOffset(2026, 5, 27, 22, 0, 0, TimeSpan.FromHours(9));
        harness.Time.SetUtcNow(now.UtcDateTime);
        await harness.Store.DispatchAsync(new AlarmEvent.ArmRequested(AlarmTime, AudioSource.SystemDefault.Instance), TestContext.Current.CancellationToken).AsTask();
        await harness.WaitForStateAsync<AlarmState.Armed>().ConfigureAwait(true);

        // Advance to fire time and dispatch a Tick — interpreter should run BeginRinging.
        var fireAt = new DateTimeOffset(2026, 5, 28, 7, 0, 0, TimeSpan.FromHours(9));
        harness.Time.SetUtcNow(fireAt.UtcDateTime);
        await harness.Store.DispatchAsync(new AlarmEvent.Tick(fireAt), TestContext.Current.CancellationToken).AsTask();
        await harness.WaitForStateAsync<AlarmState.Ringing>().ConfigureAwait(true);

        harness.Volume.LastApplied.ShouldBe(VolumeSnapshot.Maximum);
        harness.Player.IsPlaying.ShouldBeTrue();

        // Stop — interpreter should restore the captured snapshot.
        await harness.Store.DispatchAsync(AlarmEvent.StopRingingRequested.Instance, TestContext.Current.CancellationToken).AsTask();
        await harness.WaitForStateAsync<AlarmState.Idle>().ConfigureAwait(true);

        harness.Volume.LastApplied.ShouldBe(capturedSnapshot);
        harness.Player.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancel_FromArmed_GoesIdle_WithoutTouchingVolume()
    {
        await using var harness = await Harness.StartAsync().ConfigureAwait(true);

        await harness.Store.DispatchAsync(new AlarmEvent.ArmRequested(AlarmTime, AudioSource.SystemDefault.Instance), TestContext.Current.CancellationToken).AsTask();
        await harness.WaitForStateAsync<AlarmState.Armed>().ConfigureAwait(true);

        await harness.Store.DispatchAsync(AlarmEvent.CancelRequested.Instance, TestContext.Current.CancellationToken).AsTask();
        await harness.WaitForStateAsync<AlarmState.Idle>().ConfigureAwait(true);

        harness.Volume.CaptureCount.ShouldBe(0);
        harness.Volume.ApplyCount.ShouldBe(0);
        harness.Player.PlayCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task TickBeforeDue_StaysArmed_DoesNotStartPlayback()
    {
        await using var harness = await Harness.StartAsync().ConfigureAwait(true);

        var armedAt = new DateTimeOffset(2026, 5, 27, 22, 0, 0, TimeSpan.FromHours(9));
        harness.Time.SetUtcNow(armedAt.UtcDateTime);
        await harness.Store.DispatchAsync(new AlarmEvent.ArmRequested(AlarmTime, AudioSource.SystemDefault.Instance), TestContext.Current.CancellationToken).AsTask();
        await harness.WaitForStateAsync<AlarmState.Armed>().ConfigureAwait(true);

        var notYet = armedAt.AddHours(1);
        await harness.Store.DispatchAsync(new AlarmEvent.Tick(notYet), TestContext.Current.CancellationToken).AsTask();
        // Wait for the reducer to actually finish handling the tick instead of polling
        // on wall-clock time. Pending = 0 means the queue is drained.
        await harness.WaitForIdleAsync().ConfigureAwait(true);

        harness.Store.Current.ShouldBeOfType<AlarmState.Armed>();
        harness.Player.PlayCallCount.ShouldBe(0);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required AlarmStore Store { get; init; }
        public required EffectInterpreter Interpreter { get; init; }
        public required FakeAudioPlayer Player { get; init; }
        public required FakeVolumeController Volume { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required Task StoreLoop { get; init; }
        public required Task EffectLoop { get; init; }
        public required CancellationTokenSource Cts { get; init; }

        public static async Task<Harness> StartAsync()
        {
            var player = new FakeAudioPlayer();
            var volume = new FakeVolumeController();
            var time = new FakeTimeProvider();
            var store = new AlarmStore(time, NullLogger<AlarmStore>.Instance);
            var interpreter = new EffectInterpreter(store, player, volume, time, NullLogger<EffectInterpreter>.Instance);
            var cts = new CancellationTokenSource();
            // Start each loop in the test's own context so the awaiting on dispose stays
            // within the same scheduler — keeps VSTHRD003 happy.
            var storeLoop = store.RunAsync(cts.Token);
            var effectLoop = interpreter.RunAsync(cts.Token);
            await Task.Yield();
            return new Harness
            {
                Store = store,
                Interpreter = interpreter,
                Player = player,
                Volume = volume,
                Time = time,
                StoreLoop = storeLoop,
                EffectLoop = effectLoop,
                Cts = cts,
            };
        }

        public async Task WaitForStateAsync<TState>(TimeSpan? timeout = null) where TState : AlarmState
        {
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));
            try
            {
                await Store.States
                    .Where(s => s is TState)
                    .FirstAsync(cancellationToken: cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"State {typeof(TState).Name} not reached. Current = {Store.Current.GetType().Name}");
            }
        }

        public async Task WaitForIdleAsync(TimeSpan? timeout = null)
        {
            if (Store.IsIdle)
            {
                return;
            }
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));
            try
            {
                await Store.Pending
                    .Where(n => n == 0)
                    .FirstAsync(cancellationToken: cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException("Store did not drain its event queue in time.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Cts.CancelAsync().ConfigureAwait(false);
            try { await StoreLoop.ConfigureAwait(false); } catch (OperationCanceledException) { /* expected */ }
            try { await EffectLoop.ConfigureAwait(false); } catch (OperationCanceledException) { /* expected */ }
            Cts.Dispose();
            await Store.DisposeAsync().ConfigureAwait(false);
            await Interpreter.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Minimal TimeProvider that lets tests advance wall-clock time deterministically.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public void SetUtcNow(DateTime utcNow) => _utcNow = new DateTimeOffset(utcNow, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
    }
}
