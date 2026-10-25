using Alarm.Application.Ports;
using Alarm.Domain.Model;
using Microsoft.Extensions.Logging;

namespace Alarm.Infrastructure.Audio;

/// <summary>
/// Starts playback through <see cref="NAudioBackend"/> and owns the device until completion.
/// Readiness follows the successful start call, and completion follows device cleanup.
/// </summary>
internal sealed class AudioPlayer : IAudioPlayer, IAsyncDisposable
{
    private readonly NAudioBackend _backend;
    private readonly ILogger<AudioPlayer> _logger;

    private volatile int _playing;

    public AudioPlayer(NAudioBackend backend, ILogger<AudioPlayer> logger)
    {
        _backend = backend;
        _logger = logger;
    }

    public bool IsPlaying => _playing != 0;

    public PlaybackSession StartPlayback(AudioSource source, CancellationToken ct)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = Task.Run(() => PlayAsync(source, ready, ct), CancellationToken.None);
        return new PlaybackSession(ready.Task, completion);
    }

    private async Task PlayAsync(AudioSource source, TaskCompletionSource ready, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            _backend.Play(source);
            ct.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref _playing, 1);
            ready.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ready.TrySetCanceled(ct);
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
            _logger.LogError(ex, "Playback aborted with an unexpected error");
            throw;
        }
        finally
        {
            try
            {
                _backend.Stop();
            }
            finally
            {
                Interlocked.Exchange(ref _playing, 0);
            }
        }
    }

    public async ValueTask DisposeAsync() => await _backend.DisposeAsync().ConfigureAwait(false);
}
