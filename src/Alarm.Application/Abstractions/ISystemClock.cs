namespace Alarm.Application.Abstractions;

/// <summary>Wall-clock abstraction so domain/application code never touches <see cref="DateTimeOffset.Now"/> directly.</summary>
public interface ISystemClock
{
    DateTimeOffset Now { get; }
}
