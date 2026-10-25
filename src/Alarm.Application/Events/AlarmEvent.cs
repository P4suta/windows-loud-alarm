using Alarm.Domain.Common;
using Alarm.Domain.Model;

namespace Alarm.Application.Events;

/// <summary>
/// UI intents and cycle-bound effect completions drive the reducer.
/// <see cref="RingingBegan"/> follows playback readiness rather than worker scheduling.
/// </summary>
public abstract record AlarmEvent
{
    private AlarmEvent() { }

    // --- UI / external intents ---
    public sealed record ArmRequested(TimeOfDay Time, AudioSource Sound) : AlarmEvent;

    public sealed record CancelRequested : AlarmEvent
    {
        public static readonly CancelRequested Instance = new();
        private CancelRequested() { }
    }

    public sealed record StopRingingRequested : AlarmEvent
    {
        public static readonly StopRingingRequested Instance = new();
        private StopRingingRequested() { }
    }

    // --- Time progression ---
    public sealed record Tick(DateTimeOffset Now) : AlarmEvent;

    // --- Effect completion notifications ---
    public sealed record RingingBegan(AlarmSchedule Schedule, VolumeSnapshot CapturedRestorePoint) : AlarmEvent;
    public sealed record RingingEnded(AlarmSchedule Schedule, DateTimeOffset At) : AlarmEvent;
    public sealed record EffectFailed(AlarmSchedule Schedule, string EffectName, AlarmError Error) : AlarmEvent;
}
