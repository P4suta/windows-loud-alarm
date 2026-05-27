using Alarm.Application.Abstractions;

namespace Alarm.Infrastructure.Time;

internal sealed class SystemClock : ISystemClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
