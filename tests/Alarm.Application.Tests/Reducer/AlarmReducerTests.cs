using Alarm.Application.Effects;
using Alarm.Application.Events;
using Alarm.Application.Reducer;
using Alarm.Application.State;
using Alarm.Domain.Common;
using Alarm.Domain.Model;
using Shouldly;
using Xunit;

namespace Alarm.Application.Tests.Reducer;

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
        effects.Length.ShouldBe(1);
        effects[0].ShouldBe(new AlarmEffect.CancelRinging(armed.Schedule));
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

        var (next, effects) = AlarmReducer.Reduce(armed, new AlarmEvent.RingingBegan(armed.Schedule, Half), BaseNow);

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

        var (next, effects) = AlarmReducer.Reduce(ringing, new AlarmEvent.RingingEnded(ringing.Schedule, BaseNow), BaseNow);

        next.ShouldBe(AlarmState.Idle.Instance);
        effects.ShouldBeEmpty();
    }

    [Fact]
    public void Ringing_OnEffectFailed_GoesIdleAndStillTriesToRestore()
    {
        var ringing = new AlarmState.Ringing(TodaySchedule, Half);
        var failure = new AlarmEvent.EffectFailed(ringing.Schedule, "BeginRinging", new AlarmError.AudioPlaybackFailed("device gone"));

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
        var failure = new AlarmEvent.EffectFailed(armed.Schedule, "BeginRinging", new AlarmError.VolumeCaptureFailed("denied"));

        var (next, effects) = AlarmReducer.Reduce(armed, failure, BaseNow);

        next.ShouldBe(armed);
        effects.Length.ShouldBe(2);
        effects[0].ShouldBe(new AlarmEffect.CancelRinging(armed.Schedule));
        effects[1].ShouldBeOfType<AlarmEffect.NotifyError>();
    }

    [Fact]
    public void EveryStateEventPair_HasAnExplicitTransitionExpectation()
    {
        var schedule = TodaySchedule;
        AlarmState[] states = [AlarmState.Idle.Instance, new AlarmState.Armed(schedule), new AlarmState.Ringing(schedule, Half)];
        var eventTypes = typeof(AlarmEvent).GetNestedTypes().Where(type => type.IsAssignableTo(typeof(AlarmEvent))).ToArray();
        Dictionary<(Type State, Type Event), (Type Next, Type[] Effects)> expectations = new()
        {
            [(typeof(AlarmState.Idle), typeof(AlarmEvent.ArmRequested))] = (typeof(AlarmState.Armed), []),
            [(typeof(AlarmState.Idle), typeof(AlarmEvent.CancelRequested))] = (typeof(AlarmState.Idle), []),
            [(typeof(AlarmState.Idle), typeof(AlarmEvent.StopRingingRequested))] = (typeof(AlarmState.Idle), []),
            [(typeof(AlarmState.Idle), typeof(AlarmEvent.Tick))] = (typeof(AlarmState.Idle), []),
            [(typeof(AlarmState.Idle), typeof(AlarmEvent.RingingBegan))] = (typeof(AlarmState.Idle), []),
            [(typeof(AlarmState.Idle), typeof(AlarmEvent.RingingEnded))] = (typeof(AlarmState.Idle), []),
            [(typeof(AlarmState.Idle), typeof(AlarmEvent.EffectFailed))] = (typeof(AlarmState.Idle), [typeof(AlarmEffect.NotifyError)]),
            [(typeof(AlarmState.Armed), typeof(AlarmEvent.ArmRequested))] = (typeof(AlarmState.Armed), []),
            [(typeof(AlarmState.Armed), typeof(AlarmEvent.CancelRequested))] = (typeof(AlarmState.Idle), [typeof(AlarmEffect.CancelRinging)]),
            [(typeof(AlarmState.Armed), typeof(AlarmEvent.StopRingingRequested))] = (typeof(AlarmState.Armed), []),
            [(typeof(AlarmState.Armed), typeof(AlarmEvent.Tick))] = (typeof(AlarmState.Armed), [typeof(AlarmEffect.BeginRinging)]),
            [(typeof(AlarmState.Armed), typeof(AlarmEvent.RingingBegan))] = (typeof(AlarmState.Ringing), []),
            [(typeof(AlarmState.Armed), typeof(AlarmEvent.RingingEnded))] = (typeof(AlarmState.Armed), []),
            [(typeof(AlarmState.Armed), typeof(AlarmEvent.EffectFailed))] = (typeof(AlarmState.Armed), [typeof(AlarmEffect.CancelRinging), typeof(AlarmEffect.NotifyError)]),
            [(typeof(AlarmState.Ringing), typeof(AlarmEvent.ArmRequested))] = (typeof(AlarmState.Ringing), []),
            [(typeof(AlarmState.Ringing), typeof(AlarmEvent.CancelRequested))] = (typeof(AlarmState.Ringing), []),
            [(typeof(AlarmState.Ringing), typeof(AlarmEvent.StopRingingRequested))] = (typeof(AlarmState.Ringing), [typeof(AlarmEffect.EndRinging)]),
            [(typeof(AlarmState.Ringing), typeof(AlarmEvent.Tick))] = (typeof(AlarmState.Ringing), []),
            [(typeof(AlarmState.Ringing), typeof(AlarmEvent.RingingBegan))] = (typeof(AlarmState.Ringing), []),
            [(typeof(AlarmState.Ringing), typeof(AlarmEvent.RingingEnded))] = (typeof(AlarmState.Idle), []),
            [(typeof(AlarmState.Ringing), typeof(AlarmEvent.EffectFailed))] = (typeof(AlarmState.Idle), [typeof(AlarmEffect.EndRinging), typeof(AlarmEffect.NotifyError)]),
        };

        expectations.Count.ShouldBe(states.Length * eventTypes.Length);
        states.Select(state => state.GetType()).OrderBy(type => type.Name, StringComparer.Ordinal)
            .ShouldBe(typeof(AlarmState).GetNestedTypes().Where(type => type.IsAssignableTo(typeof(AlarmState))).OrderBy(type => type.Name, StringComparer.Ordinal));
        foreach (var state in states)
        {
            foreach (var eventType in eventTypes)
            {
                var expected = expectations[(state.GetType(), eventType)];
                var (next, effects) = AlarmReducer.Reduce(state, MakeEvent(eventType, schedule), schedule.FireAt);
                next.GetType().ShouldBe(expected.Next);
                effects.Select(effect => effect.GetType()).ShouldBe(expected.Effects);
            }
        }

        var coveredEffects = expectations.Values.SelectMany(expected => expected.Effects).Distinct().OrderBy(type => type.Name, StringComparer.Ordinal);
        var declaredEffects = typeof(AlarmEffect).GetNestedTypes().Where(type => type.IsAssignableTo(typeof(AlarmEffect))).OrderBy(type => type.Name, StringComparer.Ordinal);
        coveredEffects.ShouldBe(declaredEffects);
    }

    [Fact]
    public void StaleCompletion_CannotChangeANewerCycleWithEqualOrDifferentValues()
    {
        var current = TodaySchedule;
        AlarmSchedule[] staleSchedules = [TodaySchedule, AlarmSchedule.Create(TimeOfDay.Of(8, 0), AudioSource.SystemDefault.Instance, BaseNow)];
        Type[] completionTypes = [typeof(AlarmEvent.RingingBegan), typeof(AlarmEvent.RingingEnded), typeof(AlarmEvent.EffectFailed)];
        AlarmState[] states = [new AlarmState.Armed(current), new AlarmState.Ringing(current, Half)];

        staleSchedules[0].ShouldBe(current);
        foreach (var stale in staleSchedules)
        {
            ReferenceEquals(stale, current).ShouldBeFalse();
            foreach (var state in states)
            {
                foreach (var completionType in completionTypes)
                {
                    var (next, effects) = AlarmReducer.Reduce(state, MakeEvent(completionType, stale), BaseNow);
                    next.ShouldBeSameAs(state);
                    effects.ShouldBeEmpty();
                }
            }
        }
    }

    private static AlarmEvent MakeEvent(Type type, AlarmSchedule? schedule = null)
    {
        var cycle = schedule ?? TodaySchedule;
        return type switch
        {
            _ when type == typeof(AlarmEvent.ArmRequested) =>
                new AlarmEvent.ArmRequested(AlarmTime, AudioSource.SystemDefault.Instance),
            _ when type == typeof(AlarmEvent.CancelRequested) =>
                AlarmEvent.CancelRequested.Instance,
            _ when type == typeof(AlarmEvent.StopRingingRequested) =>
                AlarmEvent.StopRingingRequested.Instance,
            _ when type == typeof(AlarmEvent.Tick) =>
                new AlarmEvent.Tick(cycle.FireAt),
            _ when type == typeof(AlarmEvent.RingingBegan) =>
                new AlarmEvent.RingingBegan(cycle, Half),
            _ when type == typeof(AlarmEvent.RingingEnded) =>
                new AlarmEvent.RingingEnded(cycle, BaseNow),
            _ when type == typeof(AlarmEvent.EffectFailed) =>
                new AlarmEvent.EffectFailed(cycle, nameof(AlarmEffect.BeginRinging), new AlarmError.AudioPlaybackFailed("Device start failed.")),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Add a constructor mapping for this event type."),
        };
    }
}
