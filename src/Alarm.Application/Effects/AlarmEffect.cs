using Alarm.Domain.Common;
using Alarm.Domain.Model;

namespace Alarm.Application.Effects;

/// <summary>
/// Closed hierarchy of side effects emitted by the reducer. The interpreter handles each
/// effect; success/failure is fed back into the store as <c>AlarmEvent.RingingBegan</c>,
/// <c>RingingEnded</c>, or <c>EffectFailed</c>.
/// </summary>
/// <remarks>
/// <see cref="BeginRinging"/> and <see cref="EndRinging"/> are intentionally composite:
/// capture+max+play and stop+restore each run inside one interpreter method so that no
/// "orphaned RestorePoint" can be created if an event arrives mid-sequence.
/// </remarks>
public abstract record AlarmEffect
{
    private AlarmEffect() { }

    public sealed record BeginRinging(AlarmSchedule Schedule) : AlarmEffect;
    public sealed record EndRinging(VolumeSnapshot RestorePoint) : AlarmEffect;
    public sealed record NotifyError(AlarmError Error) : AlarmEffect;
}
