namespace Alarm.Domain.Model;

/// <summary>
/// A scheduled, one-shot alarm — the resolved absolute fire time and the chosen sound.
/// Constructed only through <see cref="Create"/> so that <see cref="FireAt"/> is always
/// derived from <see cref="Time"/> and a clock reading rather than supplied externally.
/// </summary>
public sealed record AlarmSchedule
{
    public TimeOfDay Time { get; }
    public AudioSource Sound { get; }
    public DateTimeOffset FireAt { get; }

    private AlarmSchedule(TimeOfDay time, AudioSource sound, DateTimeOffset fireAt)
    {
        Time = time;
        Sound = sound;
        FireAt = fireAt;
    }

    public static AlarmSchedule Create(TimeOfDay time, AudioSource sound, DateTimeOffset now) =>
        new(time, sound, time.NextOccurrenceAfter(now));

    public TimeSpan TimeUntil(DateTimeOffset now) => FireAt - now;

    public bool IsDue(DateTimeOffset now) => now >= FireAt;
}
