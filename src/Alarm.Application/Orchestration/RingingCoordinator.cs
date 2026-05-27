using Alarm.Application.Abstractions;
using Alarm.Domain.Events;
using Alarm.Domain.Model;
using Microsoft.Extensions.Logging;

namespace Alarm.Application.Orchestration;

/// <summary>
/// Owns the "ringing" lifecycle: when an alarm fires, snapshot the master volume,
/// pin it to 100 %, start looping playback. On stop, halt playback and restore the snapshot.
/// </summary>
/// <remarks>
/// Volume restoration is the single most important invariant of this class — failing
/// to restore on any exit path leaves the user's system volume pegged at 100 %.
/// Every mutation path therefore runs through <see cref="RestoreVolume"/>.
/// </remarks>
public sealed class RingingCoordinator(
    IAudioPlayer player,
    ISystemVolumeController volume,
    IAlarmScheduler scheduler,
    ISystemClock clock,
    ILogger<RingingCoordinator> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VolumeSnapshot? _restorePoint;
    private CancellationTokenSource? _ringCts;
    private bool _isRinging;

    public bool IsRinging => _isRinging;

    public event EventHandler? RingingStarted;
    public event EventHandler<RingingStopped>? RingingEnded;

    /// <summary>Wire the coordinator to the scheduler. Call once during startup.</summary>
    public void Attach() => scheduler.Triggered += OnAlarmTriggered;

    private async Task OnAlarmTriggered(AlarmTriggered evt)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isRinging)
            {
                logger.LogWarning("Alarm fired while already ringing — ignoring");
                return;
            }

            logger.LogInformation("Alarm fired at {At} for {Schedule}", evt.At, evt.Schedule.FireAt);

            _restorePoint = volume.Capture();
            volume.Apply(VolumeSnapshot.Maximum);

            _ringCts = new CancellationTokenSource();
            await player.PlayLoopAsync(evt.Schedule.Sound, _ringCts.Token).ConfigureAwait(false);
            _isRinging = true;
        }
        catch
        {
            RestoreVolume();
            throw;
        }
        finally
        {
            _gate.Release();
        }

        RingingStarted?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_isRinging) return;

            if (_ringCts is not null) await _ringCts.CancelAsync().ConfigureAwait(false);
            await player.StopAsync().ConfigureAwait(false);
            RestoreVolume();
            _isRinging = false;
        }
        finally
        {
            _gate.Release();
        }

        RingingEnded?.Invoke(this, new RingingStopped(clock.Now));
    }

    private void RestoreVolume()
    {
        if (_restorePoint is { } snap)
        {
            try
            {
                volume.Apply(snap);
                logger.LogInformation("Restored master volume to scalar={Scalar} muted={Muted}", snap.Scalar, snap.IsMuted);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to restore master volume");
            }
            _restorePoint = null;
        }
        _ringCts?.Dispose();
        _ringCts = null;
    }

    public async ValueTask DisposeAsync()
    {
        scheduler.Triggered -= OnAlarmTriggered;
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
