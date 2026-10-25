namespace Alarm.Application.Ports;

public sealed class PlaybackSession
{
    public PlaybackSession(Task ready, Task completion)
    {
        ArgumentNullException.ThrowIfNull(ready);
        ArgumentNullException.ThrowIfNull(completion);
        Ready = ready;
        Completion = completion;
    }

    public Task Ready { get; }
    public Task Completion { get; }
}
