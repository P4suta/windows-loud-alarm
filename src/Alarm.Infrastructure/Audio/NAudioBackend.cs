using Alarm.Domain.Model;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Alarm.Infrastructure.Audio;

/// <summary>
/// Thin synchronous wrapper around NAudio's <see cref="WaveOutEvent"/> + <see cref="LoopStream"/>
/// pair. Owns the device handles and the audio file reader for the current playback cycle.
/// All transitions are guarded by an internal lock — Start replaces any running cycle.
/// </summary>
internal sealed class NAudioBackend : IDisposable, IAsyncDisposable
{
    private readonly FallbackAudioResolver _fallback;
    private readonly ILogger<NAudioBackend> _logger;
    private readonly Lock _gate = new();

    private WaveOutEvent? _output;
    private LoopStream? _loop;

    public NAudioBackend(FallbackAudioResolver fallback, ILogger<NAudioBackend> logger)
    {
        _fallback = fallback;
        _logger = logger;
    }

    public void Play(AudioSource source)
    {
        var path = ResolvePath(source)
            ?? throw new InvalidOperationException("No audio source available — Windows default alarm sound not found.");

        lock (_gate)
        {
            StopUnsafe();

            AudioFileReader? reader = null;
            LoopStream? loop = null;
            WaveOutEvent? output = null;
            try
            {
                reader = new AudioFileReader(path);
                loop = new LoopStream(reader);
                output = new WaveOutEvent { DesiredLatency = 100 };
                output.Init(loop);
                output.Play();

                _loop = loop;
                _output = output;
                // ownership transferred — prevent finally from disposing
                reader = null;
                loop = null;
                output = null;
                _logger.LogInformation("Playback started: {Path}", path);
            }
            finally
            {
                output?.Dispose();
                loop?.Dispose();
                reader?.Dispose();
            }
        }
    }

    public void Stop()
    {
        lock (_gate) { StopUnsafe(); }
    }

    private void StopUnsafe()
    {
        if (_output is { } out_)
        {
            try { out_.Stop(); }
            catch (Exception ex) { _logger.LogDebug(ex, "WaveOutEvent.Stop threw — ignoring during teardown"); }
            out_.Dispose();
            _output = null;
        }
        _loop?.Dispose(); // disposes the wrapped AudioFileReader
        _loop = null;
    }

    private string? ResolvePath(AudioSource source) => source switch
    {
        AudioSource.UserFile uf when File.Exists(uf.Path) => uf.Path,
        AudioSource.UserFile uf => FallbackWithLog(uf.Path),
        AudioSource.SystemDefault => _fallback.TryResolve(),
        _ => null,
    };

    private string? FallbackWithLog(string missingPath)
    {
        _logger.LogWarning("User audio file not found: {Path} — falling back to Windows default", missingPath);
        return _fallback.TryResolve();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _output?.Dispose();
            _output = null;
            _loop?.Dispose();
            _loop = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
