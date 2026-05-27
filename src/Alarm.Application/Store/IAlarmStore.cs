using Alarm.Application.Events;
using Alarm.Application.State;
using R3;

namespace Alarm.Application.Store;

/// <summary>
/// The single source of truth for alarm state. Callers dispatch events; subscribers observe
/// the resulting state stream. The reducer runs on a single dedicated loop — no caller-side
/// locking is needed.
/// </summary>
public interface IAlarmStore
{
    AlarmState Current { get; }
    Observable<AlarmState> States { get; }

    ValueTask DispatchAsync(AlarmEvent evt, CancellationToken ct = default);
}
