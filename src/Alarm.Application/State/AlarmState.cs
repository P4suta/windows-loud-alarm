using Alarm.Domain.Model;

namespace Alarm.Application.State;

/// <summary>
/// Closed hierarchy describing the alarm lifecycle. <see cref="Ringing"/> carries its own
/// <see cref="VolumeSnapshot"/> so the restore value cannot be lost between Capture and Restore —
/// the invariant is enforced by the type rather than by careful imperative bookkeeping.
/// </summary>
public abstract record AlarmState
{
    private AlarmState() { }

    public sealed record Idle : AlarmState
    {
        public static readonly Idle Instance = new();
        private Idle() { }
    }

    public sealed record Armed(AlarmSchedule Schedule) : AlarmState;

    public sealed record Ringing(AlarmSchedule Schedule, VolumeSnapshot RestorePoint) : AlarmState;
}
