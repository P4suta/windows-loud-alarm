using Alarm.Domain.Model;

namespace Alarm.Domain.Events;

/// <summary>
/// Raised when the scheduler reaches an alarm's fire time.
/// </summary>
public sealed record AlarmTriggered(AlarmSchedule Schedule, DateTimeOffset At);
