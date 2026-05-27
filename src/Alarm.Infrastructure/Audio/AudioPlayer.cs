using Alarm.Application.Ports;
using Alarm.Domain.Model;
using Microsoft.Extensions.Logging;

namespace Alarm.Infrastructure.Audio;

/// <summary>
/// <see cref="IAudioPlayer"/> implementation: starts playback through <see cref="NAudioBackend"/>
/// and awaits the supplied cancellation token. The returned task completes only after the
/// backend has been stopped, so callers (e.g. the effect interpreter) can simply
/// <c>ct.Cancel()</c> + <c>await</c> the playback task to know playback has truly ended.
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

    public async Task PlayUntilCancelledAsync(AudioSource source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Interlocked.Exchange(ref _playing, 1);
        try
        {
            _backend.Play(source);
            try
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* expected — stop requested */ }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playback aborted with an unexpected error");
            throw;
        }
        finally
        {
            _backend.Stop();
            Interlocked.Exchange(ref _playing, 0);
        }
    }

    public async ValueTask DisposeAsync() => await _backend.DisposeAsync().ConfigureAwait(false);
}
