using Alarm.Application.Effects;
using Alarm.Application.Events;
using Alarm.Application.Reducer;
using Alarm.Application.State;
using Alarm.Domain.Common;
using Alarm.Domain.Model;
using Shouldly;
using Xunit;

namespace Alarm.Application.Tests.Reducer;

/// <summary>
/// Exhaustive table-driven test for the pure reducer. Each (state, event) combination is
/// either a documented transition or a documented no-op — every behaviour rule of the
/// alarm state machine lives in this file.
/// </summary>
public class AlarmReducerTests
{
    private static readonly DateTimeOffset BaseNow =
        new(2026, 5, 27, 22, 0, 0, TimeSpan.FromHours(9));

    private static readonly TimeOfDay AlarmTime = TimeOfDay.Of(7, 0);
    private static AlarmSchedule TodaySchedule => AlarmSchedule.Create(AlarmTime, AudioSource.SystemDefault.Instance, BaseNow);
    private static VolumeSnapshot Half => VolumeSnapshot.Of(0.5f, isMuted: false);

    // ───── Idle ─────

    [Fact]
    public void Idle_OnArmRequested_TransitionsToArmedWithResolvedSchedule()
    {
        var (next, effects) = AlarmReducer.Reduce(
            AlarmState.Idle.Instance,
            new AlarmEvent.ArmRequested(AlarmTime, AudioSource.SystemDefault.Instance),
            BaseNow);

        next.ShouldBeOfType<AlarmState.Armed>();
        ((AlarmState.Armed)next).Schedule.Time.ShouldBe(AlarmTime);
        ((AlarmState.Armed)next).Schedule.FireAt.ShouldBe(TodaySchedule.FireAt);
        effects.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(typeof(AlarmEvent.CancelRequested))]
    [InlineData(typeof(AlarmEvent.StopRingingRequested))]
    [InlineData(typeof(AlarmEvent.Tick))]
    [InlineData(typeof(AlarmEvent.RingingBegan))]
    [InlineData(typeof(AlarmEvent.RingingEnded))]
    public void Idle_IgnoresEverythingExceptArm(Type eventType)
    {
        var evt = MakeEvent(eventType);

        var (next, effects) = AlarmReducer.Reduce(AlarmState.Idle.Instance, evt, BaseNow);

        next.ShouldBe(AlarmState.Idle.Instance);
        effects.ShouldBeEmpty();
    }

    // ───── Armed ─────

    [Fact]
    public void Armed_OnCancel_ReturnsToIdle()
    {
        var armed = new AlarmState.Armed(TodaySchedule);

        var (next, effects) = AlarmReducer.Reduce(armed, AlarmEvent.CancelRequested.Instance, BaseNow);

        next.ShouldBe(AlarmState.Idle.Instance);
        effects.ShouldBeEmpty();
    }

    [Fact]
    public void Armed_OnTickBeforeFireAt_StaysArmedAndEmitsNoEffect()
    {
        var armed = new AlarmState.Armed(TodaySchedule);
        var earlyTick = new AlarmEvent.Tick(BaseNow);

        var (next, effects) = AlarmReducer.Reduce(armed, earlyTick, BaseNow);

        next.ShouldBe(armed);
        effects.ShouldBeEmpty();
    }

    [Fact]
    public void Armed_OnTickAtFireAt_EmitsBeginRingingEffectButRetainsArmed()
    {
        var armed = new AlarmState.Armed(TodaySchedule);
        var dueTick = new AlarmEvent.Tick(TodaySchedule.FireAt);

        var (next, effects) = AlarmReducer.Reduce(armed, dueTick, TodaySchedule.FireAt);

        next.ShouldBe(armed);
        effects.Length.ShouldBe(1);
        effects[0].ShouldBeOfType<AlarmEffect.BeginRinging>();
        ((AlarmEffect.BeginRinging)effects[0]).Schedule.ShouldBe(TodaySchedule);
    }

    [Fact]
    public void Armed_OnRingingBegan_TransitionsToRingingWithRestorePoint()
    {
        var armed = new AlarmState.Armed(TodaySchedule);

        var (next, effects) = AlarmReducer.Reduce(armed, new AlarmEvent.RingingBegan(Half), BaseNow);

        next.ShouldBeOfType<AlarmState.Ringing>();
        var ringing = (AlarmState.Ringing)next;
        ringing.Schedule.ShouldBe(TodaySchedule);
        ringing.RestorePoint.ShouldBe(Half);
        effects.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(typeof(AlarmEvent.ArmRequested))]
    [InlineData(typeof(AlarmEvent.StopRingingRequested))]
    [InlineData(typeof(AlarmEvent.RingingEnded))]
    public void Armed_IgnoresIrrelevantEvents(Type eventType)
    {
        var armed = new AlarmState.Armed(TodaySchedule);
        var evt = MakeEvent(eventType);

        var (next, effects) = AlarmReducer.Reduce(armed, evt, BaseNow);

        next.ShouldBe(armed);
        effects.ShouldBeEmpty();
    }

    // ───── Ringing ─────

    [Fact]
    public void Ringing_OnStopRinging_EmitsEndRingingEffectButRetainsRinging()
    {
        var ringing = new AlarmState.Ringing(TodaySchedule, Half);

        var (next, effects) = AlarmReducer.Reduce(ringing, AlarmEvent.StopRingingRequested.Instance, BaseNow);

        next.ShouldBe(ringing);
        effects.Length.ShouldBe(1);
        effects[0].ShouldBeOfType<AlarmEffect.EndRinging>();
        ((AlarmEffect.EndRinging)effects[0]).RestorePoint.ShouldBe(Half);
    }

    [Fact]
    public void Ringing_OnRingingEnded_ReturnsToIdle()
    {
        var ringing = new AlarmState.Ringing(TodaySchedule, Half);

        var (next, effects) = AlarmReducer.Reduce(ringing, new AlarmEvent.RingingEnded(BaseNow), BaseNow);

        next.ShouldBe(AlarmState.Idle.Instance);
        effects.ShouldBeEmpty();
    }

    [Fact]
    public void Ringing_OnEffectFailed_GoesIdleAndStillTriesToRestore()
    {
        var ringing = new AlarmState.Ringing(TodaySchedule, Half);
        var failure = new AlarmEvent.EffectFailed("BeginRinging", new AlarmError.AudioPlaybackFailed("device gone"));

        var (next, effects) = AlarmReducer.Reduce(ringing, failure, BaseNow);

        next.ShouldBe(AlarmState.Idle.Instance);
        effects.Length.ShouldBe(2);
        effects[0].ShouldBeOfType<AlarmEffect.EndRinging>();
        ((AlarmEffect.EndRinging)effects[0]).RestorePoint.ShouldBe(Half);
        effects[1].ShouldBeOfType<AlarmEffect.NotifyError>();
    }

    [Theory]
    [InlineData(typeof(AlarmEvent.ArmRequested))]
    [InlineData(typeof(AlarmEvent.CancelRequested))]
    [InlineData(typeof(AlarmEvent.Tick))]
    [InlineData(typeof(AlarmEvent.RingingBegan))]
    public void Ringing_IgnoresIrrelevantEvents(Type eventType)
    {
        var ringing = new AlarmState.Ringing(TodaySchedule, Half);
        var evt = MakeEvent(eventType);

        var (next, effects) = AlarmReducer.Reduce(ringing, evt, BaseNow);

        next.ShouldBe(ringing);
        effects.ShouldBeEmpty();
    }

    // ───── Non-ringing effect failure ─────

    [Fact]
    public void IdleOrArmed_OnEffectFailed_EmitsNotifyErrorWithoutStateChange()
    {
        var armed = new AlarmState.Armed(TodaySchedule);
        var failure = new AlarmEvent.EffectFailed("BeginRinging", new AlarmError.VolumeCaptureFailed("denied"));

        var (next, effects) = AlarmReducer.Reduce(armed, failure, BaseNow);

        next.ShouldBe(armed);
        effects.Length.ShouldBe(1);
        effects[0].ShouldBeOfType<AlarmEffect.NotifyError>();
    }

    // ───── helpers ─────

    private static AlarmEvent MakeEvent(Type type) => type switch
    {
        _ when type == typeof(AlarmEvent.ArmRequested) =>
            new AlarmEvent.ArmRequested(AlarmTime, AudioSource.SystemDefault.Instance),
        _ when type == typeof(AlarmEvent.CancelRequested) =>
            AlarmEvent.CancelRequested.Instance,
        _ when type == typeof(AlarmEvent.StopRingingRequested) =>
            AlarmEvent.StopRingingRequested.Instance,
        _ when type == typeof(AlarmEvent.Tick) =>
            new AlarmEvent.Tick(BaseNow),
        _ when type == typeof(AlarmEvent.RingingBegan) =>
            new AlarmEvent.RingingBegan(Half),
        _ when type == typeof(AlarmEvent.RingingEnded) =>
            new AlarmEvent.RingingEnded(BaseNow),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Add a constructor mapping for this event type."),
    };
}
