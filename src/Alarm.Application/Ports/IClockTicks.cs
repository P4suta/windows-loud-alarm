using R3;

namespace Alarm.Application.Ports;

/// <summary>
/// 1-Hz wall-clock stream used by the UI to refresh the displayed time and countdown.
/// Not connected to the state machine — that uses the channel-driven Tick event instead.
/// </summary>
public interface IClockTicks
{
    Observable<DateTimeOffset> Stream { get; }
}
