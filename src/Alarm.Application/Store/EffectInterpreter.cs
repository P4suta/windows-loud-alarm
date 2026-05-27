using Alarm.Application.Effects;
using Alarm.Application.Events;
using Alarm.Application.Ports;
using Alarm.Domain.Common;
using Alarm.Domain.Model;
using Microsoft.Extensions.Logging;

namespace Alarm.Application.Store;

/// <summary>
/// Translates declarative <see cref="AlarmEffect"/> values into port invocations. Each
/// interpretation is composite-and-atomic-by-design: a <c>BeginRinging</c> performs
/// capture → max → start-play and only then dispatches <c>RingingBegan</c>, so a Stop
/// arriving mid-sequence cannot strand a captured volume snapshot.
/// </summary>
internal sealed class EffectInterpreter : IAsyncDisposable
{
    private readonly AlarmStore _store;
    private readonly IAudioPlayer _player;
    private readonly ISystemVolumeController _volume;
    private readonly TimeProvider _time;
    private readonly ILogger<EffectInterpreter> _logger;

    private CancellationTokenSource? _ringCts;
    private Task? _playbackTask;

    public EffectInterpreter(AlarmStore store, IAudioPlayer player, ISystemVolumeController volume, TimeProvider time, ILogger<EffectInterpreter> logger)
    {
        _store = store;
        _player = player;
        _volume = volume;
        _time = time;
        _logger = logger;
    }

    /// <summary>Drives the effect loop. Returns when the effect channel completes or ct is cancelled.</summary>
    internal async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var fx in _store.EffectReader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await DispatchEffectAsync(fx, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
    }

    private async Task DispatchEffectAsync(AlarmEffect fx, CancellationToken ct)
    {
        switch (fx)
        {
            case AlarmEffect.BeginRinging br:
                await BeginRingingAsync(br.Schedule, ct).ConfigureAwait(false);
                break;
            case AlarmEffect.EndRinging er:
                await EndRingingAsync(er.RestorePoint).ConfigureAwait(false);
                break;
            case AlarmEffect.NotifyError ne:
                _logger.LogWarning("Alarm error surfaced: {Error}", ne.Error);
                break;
            default:
                _logger.LogWarning("Unhandled effect {Effect}", fx.GetType().Name);
                break;
        }
    }

    private async Task BeginRingingAsync(AlarmSchedule schedule, CancellationToken outer)
    {
        VolumeSnapshot snapshot;
        try
        {
            snapshot = _volume.Capture();
            _volume.Apply(VolumeSnapshot.Maximum);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture/apply volume before ringing");
            await _store.DispatchAsync(new AlarmEvent.EffectFailed(nameof(AlarmEffect.BeginRinging), AlarmError.From(ex)), CancellationToken.None).ConfigureAwait(false);
            return;
        }

        if (_ringCts is { } prevCts) await prevCts.CancelAsync().ConfigureAwait(false);
        _ringCts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        var token = _ringCts.Token;

        _playbackTask = Task.Run(async () =>
        {
            try
            {
                await _player.PlayUntilCancelledAsync(schedule.Sound, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* expected on stop */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Playback failed");
                await _store.DispatchAsync(new AlarmEvent.EffectFailed(nameof(AlarmEffect.BeginRinging), AlarmError.From(ex)), CancellationToken.None).ConfigureAwait(false);
            }
        }, CancellationToken.None);

        await _store.DispatchAsync(new AlarmEvent.RingingBegan(snapshot), CancellationToken.None).ConfigureAwait(false);
    }

    private async Task EndRingingAsync(VolumeSnapshot restore)
    {
        try
        {
            if (_ringCts is { } cts) await cts.CancelAsync().ConfigureAwait(false);
            if (_playbackTask is { } t)
            {
                try { await t.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* expected */ }
            }
            _volume.Apply(restore);
            await _store.DispatchAsync(new AlarmEvent.RingingEnded(_time.GetLocalNow()), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop ringing or restore volume");
            await _store.DispatchAsync(new AlarmEvent.RingingEnded(_time.GetLocalNow()), CancellationToken.None).ConfigureAwait(false);
            await _store.DispatchAsync(new AlarmEvent.EffectFailed(nameof(AlarmEffect.EndRinging), new AlarmError.VolumeRestoreFailed(ex.Message)), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _ringCts?.Dispose();
            _ringCts = null;
            _playbackTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_ringCts is { } cts) await cts.CancelAsync().ConfigureAwait(false);
        if (_playbackTask is { } t)
        {
            try { await t.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex) { _logger.LogDebug(ex, "Playback task threw during dispose"); }
        }
        _ringCts?.Dispose();
        _ringCts = null;
        _playbackTask = null;
    }
}
