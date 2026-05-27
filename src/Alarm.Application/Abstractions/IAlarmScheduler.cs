using Alarm.Domain.Events;
using Alarm.Domain.Model;

namespace Alarm.Application.Abstractions;

/// <summary>
/// Single-shot scheduler that raises <see cref="Triggered"/> when an armed alarm becomes due.
/// At most one schedule is armed at a time.
/// </summary>
public interface IAlarmScheduler
{
    AlarmSchedule? Current { get; }

    Task ArmAsync(AlarmSchedule schedule, CancellationToken ct);
    Task CancelAsync();

    /// <summary>
    /// Fired exactly once per armed schedule when its <c>FireAt</c> moment is reached.
    /// Handlers may be async; await all attached handlers before considering the trigger complete.
    /// </summary>
    event Func<AlarmTriggered, Task>? Triggered;
}
