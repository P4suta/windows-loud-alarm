using Alarm.Application.Abstractions;
using Alarm.Domain.Model;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Alarm.Infrastructure.Audio;

internal sealed class NAudioPlayer(
    FallbackAudioResolver fallback,
    ILogger<NAudioPlayer> logger) : IAudioPlayer, IDisposable
{
    private readonly Lock _sync = new();
    private WaveOutEvent? _output;
    private LoopStream? _loop;

    public bool IsPlaying
    {
        get
        {
            lock (_sync) return _output is { PlaybackState: PlaybackState.Playing };
        }
    }

    public Task PlayLoopAsync(AudioSource source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var path = ResolvePath(source)
            ?? throw new InvalidOperationException("No audio source available — Windows default alarm sound not found.");

        AudioFileReader? reader = null;
        LoopStream? loop = null;
        WaveOutEvent? output = null;
        try
        {
            reader = new AudioFileReader(path);
            loop = new LoopStream(reader);
            output = new WaveOutEvent { DesiredLatency = 100 };
            output.Init(loop);

            lock (_sync)
            {
                DisposeUnsafe();
                _loop = loop;
                _output = output;
                output.Play();
            }
            logger.LogInformation("Playback started: {Path}", path);

            ct.Register(() =>
            {
                try { _ = StopAsync(); }
                catch (Exception ex) { logger.LogError(ex, "Stop on cancel failed"); }
            });

            // ownership transferred into the fields under lock
            reader = null;
            loop = null;
            output = null;
            return Task.CompletedTask;
        }
        finally
        {
            // NAudio's Wave types pre-date IAsyncDisposable; sync Dispose is the only option.
#pragma warning disable CA1849, VSTHRD103 // synchronous Dispose intentional
            output?.Dispose();
            loop?.Dispose();
            reader?.Dispose();
#pragma warning restore CA1849, VSTHRD103
        }
    }

    public Task StopAsync()
    {
        lock (_sync) DisposeUnsafe();
        return Task.CompletedTask;
    }

    private void DisposeUnsafe()
    {
        if (_output is not null)
        {
            try { _output.Stop(); }
            catch (Exception ex) { logger.LogDebug(ex, "WaveOutEvent.Stop threw — ignoring during teardown"); }
            _output.Dispose();
            _output = null;
        }
        _loop?.Dispose(); // disposes the wrapped AudioFileReader
        _loop = null;
    }

    private string? ResolvePath(AudioSource source) => source switch
    {
        AudioSource.UserFile uf when File.Exists(uf.Path) => uf.Path,
        AudioSource.UserFile uf => FallbackWithLog(uf.Path),
        AudioSource.SystemDefault => fallback.TryResolve(),
        _ => null,
    };

    private string? FallbackWithLog(string missingPath)
    {
        logger.LogWarning("User audio file not found: {Path} — falling back to Windows default", missingPath);
        return fallback.TryResolve();
    }

    public void Dispose()
    {
        lock (_sync) DisposeUnsafe();
    }
}
