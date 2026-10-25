using Alarm.Application.Effects;
using Alarm.Application.Events;
using Alarm.Application.State;
using Alarm.Domain.Model;

namespace Alarm.Application.Reducer;

/// <summary>
/// Purely reduces a state/event pair to a transition and its effects.
/// Unmatched pairs return <see cref="Transition.NoOp"/>.
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
            (AlarmState.Armed a, AlarmEvent.CancelRequested) =>
                Transition.Of(AlarmState.Idle.Instance, new AlarmEffect.CancelRinging(a.Schedule)),

            (AlarmState.Armed a, AlarmEvent.Tick t) when a.Schedule.IsDue(t.Now) =>
                Transition.Of(a, new AlarmEffect.BeginRinging(a.Schedule)),

            (AlarmState.Armed a, AlarmEvent.RingingBegan rb) when ReferenceEquals(a.Schedule, rb.Schedule) =>
                Transition.Of(new AlarmState.Ringing(a.Schedule, rb.CapturedRestorePoint)),

            // --- Ringing ---
            (AlarmState.Ringing r, AlarmEvent.StopRingingRequested) =>
                Transition.Of(r, new AlarmEffect.EndRinging(r.Schedule, r.RestorePoint)),

            (AlarmState.Ringing r, AlarmEvent.RingingEnded ended) when ReferenceEquals(r.Schedule, ended.Schedule) =>
                Transition.Of(AlarmState.Idle.Instance),

            // Effect failure during Ringing: drop back to Idle and still attempt a restore.
            (AlarmState.Ringing r, AlarmEvent.EffectFailed f) when ReferenceEquals(r.Schedule, f.Schedule) =>
                Transition.Of(AlarmState.Idle.Instance,
                    new AlarmEffect.EndRinging(r.Schedule, r.RestorePoint),
                    new AlarmEffect.NotifyError(f.Error)),

            (AlarmState.Armed a, AlarmEvent.EffectFailed f) when ReferenceEquals(a.Schedule, f.Schedule) =>
                Transition.Of(a, new AlarmEffect.CancelRinging(a.Schedule), new AlarmEffect.NotifyError(f.Error)),

            (AlarmState.Idle, AlarmEvent.EffectFailed f) =>
                Transition.Of(state, new AlarmEffect.NotifyError(f.Error)),

            // All other combinations are intentional no-ops.
            _ => Transition.NoOp(state),
        };
}
