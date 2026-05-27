using Alarm.Domain.Model;

namespace Alarm.Application.Abstractions;

/// <summary>Loops an <see cref="AudioSource"/> until cancelled.</summary>
public interface IAudioPlayer
{
    bool IsPlaying { get; }

    /// <summary>
    /// Begin looping playback of <paramref name="source"/>. The task completes once playback has
    /// started (not when it ends); callers cancel via <paramref name="ct"/> or by calling <see cref="StopAsync"/>.
    /// </summary>
    Task PlayLoopAsync(AudioSource source, CancellationToken ct);

    Task StopAsync();
}
