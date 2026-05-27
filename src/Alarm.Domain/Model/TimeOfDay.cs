namespace Alarm.Domain.Model;

/// <summary>
/// Wall-clock time of day (0:00:00 – 23:59:59) — a calendar-free value object.
/// </summary>
public readonly record struct TimeOfDay
{
    public int Hour { get; }
    public int Minute { get; }
    public int Second { get; }

    private TimeOfDay(int hour, int minute, int second)
    {
        Hour = hour;
        Minute = minute;
        Second = second;
    }

    public static TimeOfDay Of(int hour, int minute, int second = 0) =>
        hour is < 0 or > 23
            ? throw new ArgumentOutOfRangeException(nameof(hour), hour, "Hour must be 0–23.")
            : minute is < 0 or > 59
            ? throw new ArgumentOutOfRangeException(nameof(minute), minute, "Minute must be 0–59.")
            : second is < 0 or > 59
            ? throw new ArgumentOutOfRangeException(nameof(second), second, "Second must be 0–59.")
            : new TimeOfDay(hour, minute, second);

    public static TimeOfDay FromTimeSpan(TimeSpan ts) =>
        Of(ts.Hours, ts.Minutes, ts.Seconds);

    public TimeSpan ToTimeSpan() => new(Hour, Minute, Second);

    /// <summary>
    /// Resolve the next absolute <see cref="DateTimeOffset"/> at which this time-of-day
    /// occurs after <paramref name="now"/>. Today if still in the future, otherwise tomorrow.
    /// </summary>
    public DateTimeOffset NextOccurrenceAfter(DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, Hour, Minute, Second, now.Offset);
        return today > now ? today : today.AddDays(1);
    }

    public override string ToString() => $"{Hour:D2}:{Minute:D2}";
}
