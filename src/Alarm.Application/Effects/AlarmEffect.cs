using Alarm.Domain.Common;
using Alarm.Domain.Model;

namespace Alarm.Application.Effects;

/// <summary>
/// Describes owned playback operations and error notifications.
/// The interpreter reports cycle-bound readiness, completion, and failure events.
/// </summary>
/// <remarks>
/// Readiness observers leave the effect consumer available for cancellation.
/// Stop and cancellation join owned work before restoring captured volume.
/// </remarks>
public abstract record AlarmEffect
{
    private AlarmEffect() { }

    public sealed record BeginRinging(AlarmSchedule Schedule) : AlarmEffect;
    public sealed record EndRinging(AlarmSchedule Schedule, VolumeSnapshot RestorePoint) : AlarmEffect;

    public sealed record CancelRinging(AlarmSchedule Schedule) : AlarmEffect;
    public sealed record NotifyError(AlarmError Error) : AlarmEffect;
}
