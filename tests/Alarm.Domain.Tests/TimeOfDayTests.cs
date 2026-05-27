using Alarm.Domain.Model;
using Shouldly;
using Xunit;

namespace Alarm.Domain.Tests;

public class TimeOfDayTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(23, 59, 59)]
    [InlineData(7, 30, 0)]
    public void Of_AcceptsValuesInRange(int h, int m, int s)
    {
        var t = TimeOfDay.Of(h, m, s);
        t.Hour.ShouldBe(h);
        t.Minute.ShouldBe(m);
        t.Second.ShouldBe(s);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(24, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 60, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(0, 0, 60)]
    public void Of_RejectsOutOfRange(int h, int m, int s) => Should.Throw<ArgumentOutOfRangeException>(() => TimeOfDay.Of(h, m, s));

    [Fact]
    public void NextOccurrenceAfter_ReturnsTodayWhenStillInFuture()
    {
        var time = TimeOfDay.Of(15, 30);
        var now = new DateTimeOffset(2026, 5, 27, 10, 0, 0, TimeSpan.FromHours(9));

        var next = time.NextOccurrenceAfter(now);

        next.ShouldBe(new DateTimeOffset(2026, 5, 27, 15, 30, 0, TimeSpan.FromHours(9)));
    }

    [Fact]
    public void NextOccurrenceAfter_RollsOverToTomorrowWhenAlreadyPast()
    {
        var time = TimeOfDay.Of(7, 0);
        var now = new DateTimeOffset(2026, 5, 27, 10, 0, 0, TimeSpan.FromHours(9));

        var next = time.NextOccurrenceAfter(now);

        next.ShouldBe(new DateTimeOffset(2026, 5, 28, 7, 0, 0, TimeSpan.FromHours(9)));
    }

    [Fact]
    public void NextOccurrenceAfter_TreatsEqualAsAlreadyOccurred()
    {
        // The contract is "after now", not "at-or-after now", so equality rolls forward.
        var time = TimeOfDay.Of(10, 0);
        var now = new DateTimeOffset(2026, 5, 27, 10, 0, 0, TimeSpan.FromHours(9));

        var next = time.NextOccurrenceAfter(now);

        next.ShouldBe(new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.FromHours(9)));
    }
}
