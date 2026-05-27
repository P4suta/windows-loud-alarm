namespace Alarm.Domain.Model;

/// <summary>
/// A scheduled, one-shot alarm — the resolved absolute fire time and the chosen sound.
/// </summary>
public sealed record AlarmSchedule(TimeOfDay Time, AudioSource Sound, DateTimeOffset FireAt)
{
    public static AlarmSchedule Create(TimeOfDay time, AudioSource sound, DateTimeOffset now) =>
        new(time, sound, time.NextOccurrenceAfter(now));

    public TimeSpan TimeUntil(DateTimeOffset now) => FireAt - now;

    public bool IsDue(DateTimeOffset now) => now >= FireAt;
}
