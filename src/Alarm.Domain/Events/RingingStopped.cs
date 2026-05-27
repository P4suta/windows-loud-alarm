namespace Alarm.Domain.Events;

/// <summary>
/// Raised after the ringing sequence (audio playback + volume override) has been stopped and reverted.
/// </summary>
public sealed record RingingStopped(DateTimeOffset At);
