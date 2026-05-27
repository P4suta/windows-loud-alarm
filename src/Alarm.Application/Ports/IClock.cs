namespace Alarm.Application.Ports;

/// <summary>Wall-clock abstraction so domain/application code never touches <see cref="DateTimeOffset.Now"/> directly.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}
