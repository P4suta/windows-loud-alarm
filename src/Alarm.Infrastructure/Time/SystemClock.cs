using Alarm.Application.Ports;

namespace Alarm.Infrastructure.Time;

/// <summary>Wraps <see cref="TimeProvider"/> so tests can substitute a virtual clock.</summary>
internal sealed class SystemClock : IClock
{
    private readonly TimeProvider _time;

    public SystemClock(TimeProvider time)
    {
        _time = time;
    }

    public DateTimeOffset Now => _time.GetLocalNow();
}
