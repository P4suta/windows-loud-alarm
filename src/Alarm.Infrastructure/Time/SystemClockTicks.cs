using Alarm.Application.Ports;
using R3;

namespace Alarm.Infrastructure.Time;

/// <summary>
/// 1 Hz cold observable backed by <see cref="TimeProvider"/>. Each subscriber gets its own
/// timer; subscribers should share via <c>Publish</c>/<c>ConnectAsync</c> if many copies are
/// undesirable. The single ViewModel subscriber doesn't need that here.
/// </summary>
internal sealed class SystemClockTicks : IClockTicks
{
    public Observable<DateTimeOffset> Stream { get; }

    public SystemClockTicks(TimeProvider time)
    {
        Stream = Observable
            .Interval(TimeSpan.FromSeconds(1), time)
            .Select(time, (_, t) => t.GetLocalNow());
    }
}
