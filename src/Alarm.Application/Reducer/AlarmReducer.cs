using Alarm.Application.Effects;
using Alarm.Application.Events;
using Alarm.Application.State;
using Alarm.Domain.Model;

namespace Alarm.Application.Reducer;

/// <summary>
/// Pure state transition function. Every legal transition is a single switch arm; anything
/// not matched falls through to <see cref="Transition.NoOp"/> — invalid commands are quietly
/// ignored rather than throwing, because state-machine inputs come from external sources
/// (timers, effect completions) that may legitimately race with each other.
/// </summary>
public static class AlarmReducer
{
    public static Transition Reduce(AlarmState state, AlarmEvent evt, DateTimeOffset now) =>
        (state, evt) switch
        {
            // --- Idle ---
            (AlarmState.Idle, AlarmEvent.ArmRequested req) =>
                Transition.Of(new AlarmState.Armed(AlarmSchedule.Create(req.Time, req.Sound, now))),

            // --- Armed ---
            (AlarmState.Armed, AlarmEvent.CancelRequested) =>
                Transition.Of(AlarmState.Idle.Instance),

            (AlarmState.Armed a, AlarmEvent.Tick t) when a.Schedule.IsDue(t.Now) =>
                Transition.Of(a, new AlarmEffect.BeginRinging(a.Schedule)),

            (AlarmState.Armed a, AlarmEvent.RingingBegan rb) =>
                Transition.Of(new AlarmState.Ringing(a.Schedule, rb.CapturedRestorePoint)),

            // --- Ringing ---
            (AlarmState.Ringing r, AlarmEvent.StopRingingRequested) =>
                Transition.Of(r, new AlarmEffect.EndRinging(r.RestorePoint)),

            (AlarmState.Ringing, AlarmEvent.RingingEnded) =>
                Transition.Of(AlarmState.Idle.Instance),

            // Effect failure during Ringing: drop back to Idle and still attempt a restore.
            (AlarmState.Ringing r, AlarmEvent.EffectFailed f) =>
                Transition.Of(AlarmState.Idle.Instance,
                    new AlarmEffect.EndRinging(r.RestorePoint),
                    new AlarmEffect.NotifyError(f.Error)),

            // Effect failure anywhere else: state unchanged, surface the error.
            (_, AlarmEvent.EffectFailed f) =>
                Transition.Of(state, new AlarmEffect.NotifyError(f.Error)),

            // All other combinations are intentional no-ops.
            _ => Transition.NoOp(state),
        };
}
