using Alarm.Application.Ports;
using Alarm.Domain.Model;

namespace Alarm.Application.Tests.Fakes;

internal sealed class FakeAudioPlayer : IAudioPlayer
{
    public bool IsPlaying { get; private set; }
    public AudioSource? LastPlayed { get; private set; }
    public int PlayCallCount { get; private set; }

    public async Task PlayUntilCancelledAsync(AudioSource source, CancellationToken ct)
    {
        IsPlaying = true;
        LastPlayed = source;
        PlayCallCount++;
        try
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* expected stop */ }
        finally
        {
            IsPlaying = false;
        }
    }
}
