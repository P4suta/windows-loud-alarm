using Alarm.Domain.Model;
using Shouldly;
using Xunit;

namespace Alarm.Domain.Tests;

public class AlarmScheduleTests
{
    [Fact]
    public void Create_ResolvesFireAtFromNextOccurrence()
    {
        var time = TimeOfDay.Of(7, 0);
        var now = new DateTimeOffset(2026, 5, 27, 22, 0, 0, TimeSpan.FromHours(9));

        var schedule = AlarmSchedule.Create(time, AudioSource.SystemDefault.Instance, now);

        schedule.Time.ShouldBe(time);
        schedule.Sound.ShouldBe(AudioSource.SystemDefault.Instance);
        schedule.FireAt.ShouldBe(new DateTimeOffset(2026, 5, 28, 7, 0, 0, TimeSpan.FromHours(9)));
    }

    [Fact]
    public void IsDue_FalseBeforeFireAt()
    {
        var time = TimeOfDay.Of(7, 0);
        var armed = new DateTimeOffset(2026, 5, 27, 22, 0, 0, TimeSpan.FromHours(9));
        var schedule = AlarmSchedule.Create(time, AudioSource.SystemDefault.Instance, armed);

        schedule.IsDue(armed).ShouldBeFalse();
        schedule.IsDue(armed.AddHours(1)).ShouldBeFalse();
    }

    [Fact]
    public void IsDue_TrueAtAndAfterFireAt()
    {
        var time = TimeOfDay.Of(7, 0);
        var armed = new DateTimeOffset(2026, 5, 27, 22, 0, 0, TimeSpan.FromHours(9));
        var schedule = AlarmSchedule.Create(time, AudioSource.SystemDefault.Instance, armed);

        schedule.IsDue(schedule.FireAt).ShouldBeTrue();
        schedule.IsDue(schedule.FireAt.AddSeconds(1)).ShouldBeTrue();
    }

    [Fact]
    public void TimeUntil_IsTheFireAtMinusNow()
    {
        var time = TimeOfDay.Of(7, 0);
        var armed = new DateTimeOffset(2026, 5, 27, 22, 0, 0, TimeSpan.FromHours(9));
        var schedule = AlarmSchedule.Create(time, AudioSource.SystemDefault.Instance, armed);

        schedule.TimeUntil(armed).ShouldBe(TimeSpan.FromHours(9));
    }
}
