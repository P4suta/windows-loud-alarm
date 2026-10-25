using Alarm.Domain.Model;

namespace Alarm.Application.Ports;

/// <summary>
/// Starts an <see cref="AudioSource"/> without blocking the caller.
/// The session separates accepted playback from fully stopped playback.
/// </summary>
public interface IAudioPlayer
{
    bool IsPlaying { get; }

    /// <summary>
    /// Readiness completes after playback starts, or fails when startup fails.
    /// Cancellation settles readiness and completion, including cancellation before startup.
    /// Completion accounts for every owned worker and device handle.
    /// Session tasks must complete without UI-thread access.
    /// </summary>
    PlaybackSession StartPlayback(AudioSource source, CancellationToken ct);
}
