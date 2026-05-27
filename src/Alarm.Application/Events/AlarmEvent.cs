using Alarm.Domain.Common;
using Alarm.Domain.Model;

namespace Alarm.Application.Events;

/// <summary>
/// Closed hierarchy of inputs to the alarm reducer. UI intents and effect completions
/// are both events — the reducer treats them uniformly, which is how a chain of effects
/// (capture → set max → start play → "RingingBegan") composes into a single state transition.
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
    public sealed record RingingBegan(VolumeSnapshot CapturedRestorePoint) : AlarmEvent;
    public sealed record RingingEnded(DateTimeOffset At) : AlarmEvent;
    public sealed record EffectFailed(string EffectName, AlarmError Error) : AlarmEvent;
}
