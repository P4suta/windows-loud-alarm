using Alarm.Domain.Model;

namespace Alarm.Application.Ports;

/// <summary>
/// Loops an <see cref="AudioSource"/> until cancelled. The returned task completes when
/// playback has fully stopped — caller cancels via the token and may then await stop.
/// </summary>
public interface IAudioPlayer
{
    bool IsPlaying { get; }

    /// <summary>
    /// Begin looping playback and complete only when the loop has fully stopped — either
    /// because <paramref name="ct"/> was cancelled or the underlying source ended naturally.
    /// Cancellation is the sole stop signal: callers cancel the token and await the same task.
    /// </summary>
    Task PlayUntilCancelledAsync(AudioSource source, CancellationToken ct);
}
