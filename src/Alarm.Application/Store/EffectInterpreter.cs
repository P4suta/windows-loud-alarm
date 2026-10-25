using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Alarm.Application.Effects;
using Alarm.Application.Events;
using Alarm.Application.Ports;
using Alarm.Domain.Common;
using Alarm.Domain.Model;
using Microsoft.Extensions.Logging;

namespace Alarm.Application.Store;

/// <summary>
/// Owns playback startup, cancellation, and the captured volume until restoration.
/// Readiness observers leave the effect consumer available for cancellation.
/// </summary>
internal sealed class EffectInterpreter : IAsyncDisposable
{
    private readonly IAlarmStore _store;
    private readonly ChannelReader<AlarmEffect> _effects;
    private readonly IAudioPlayer _player;
    private readonly ISystemVolumeController _volume;
    private readonly TimeProvider _time;
    private readonly ILogger<EffectInterpreter> _logger;

    private PlaybackOperation? _operation;
    private readonly Lock _lifecycleGate = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private Task? _disposeTask;

    public EffectInterpreter(AlarmStore store, IAudioPlayer player, ISystemVolumeController volume, TimeProvider time, ILogger<EffectInterpreter> logger)
        : this(store, store.EffectReader, player, volume, time, logger)
    {
    }

    internal EffectInterpreter(IAlarmStore store, ChannelReader<AlarmEffect> effects, IAudioPlayer player, ISystemVolumeController volume, TimeProvider time, ILogger<EffectInterpreter> logger)
    {
        _store = store;
        _effects = effects;
        _player = player;
        _volume = volume;
        _time = time;
        _logger = logger;
    }

    /// <summary>Processes effects until channel completion or cancellation.</summary>
    internal Task RunAsync(CancellationToken ct)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (_runTask is not null)
            {
                throw new InvalidOperationException("The interpreter can only run once.");
            }

            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runTask = RunCoreAsync(_runCancellation.Token);
            return _runTask;
        }
    }

    private async Task RunCoreAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var fx in _effects.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await DispatchEffectAsync(fx, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        finally
        {
            if (_operation is { } operation)
            {
                await StopOperationAsync(operation.Schedule, notify: false).ConfigureAwait(false);
            }
        }
    }

    private async Task DispatchEffectAsync(AlarmEffect fx, CancellationToken ct)
    {
        switch (fx)
        {
            case AlarmEffect.BeginRinging br:
                await BeginRingingAsync(br.Schedule, ct).ConfigureAwait(false);
                break;
            case AlarmEffect.EndRinging er:
                await StopOperationAsync(er.Schedule, notify: true).ConfigureAwait(false);
                break;
            case AlarmEffect.CancelRinging cancel:
                await StopOperationAsync(cancel.Schedule, notify: true).ConfigureAwait(false);
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
        if (_operation is not null)
        {
            return;
        }

        VolumeSnapshot snapshot;
        try
        {
            snapshot = _volume.Capture();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture volume before ringing");
            await ReportFailureAsync(schedule, nameof(AlarmEffect.BeginRinging), AlarmError.From(ex)).ConfigureAwait(false);
            return;
        }

        var operation = new PlaybackOperation(schedule, snapshot, outer);
        _operation = operation;
        try
        {
            _volume.Apply(VolumeSnapshot.Maximum);
            var session = _player.StartPlayback(schedule.Sound, operation.Cancellation.Token);
            operation.Session = session;
            operation.Observer = ObservePlaybackAsync(operation, session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start playback");
            await StopOperationAsync(schedule, notify: true).ConfigureAwait(false);
            await ReportFailureAsync(schedule, nameof(AlarmEffect.BeginRinging), AlarmError.From(ex)).ConfigureAwait(false);
        }
    }

    private async Task ObservePlaybackAsync(PlaybackOperation operation, PlaybackSession session)
    {
        var token = operation.Cancellation.Token;
        try
        {
            await session.Ready.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await _store.DispatchAsync(new AlarmEvent.RingingBegan(operation.Schedule, operation.Snapshot), CancellationToken.None).ConfigureAwait(false);
            await session.Completion.ConfigureAwait(false);
            if (!token.IsCancellationRequested)
            {
                await ReportFailureAsync(operation.Schedule, nameof(AlarmEffect.BeginRinging), new AlarmError.AudioPlaybackFailed("Playback ended before cancellation.")).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            operation.ReportedFailure = ex;
            _logger.LogError(ex, "Playback failed");
            await ReportFailureAsync(operation.Schedule, nameof(AlarmEffect.BeginRinging), AlarmError.From(ex)).ConfigureAwait(false);
        }
    }

    private async Task StopOperationAsync(AlarmSchedule schedule, bool notify)
    {
        if (_operation is not { } operation || !ReferenceEquals(operation.Schedule, schedule))
        {
            return;
        }

        Exception? cancellationFailure = null;
        try
        {
            await operation.Cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            cancellationFailure = ex;
            _logger.LogError(ex, "Playback cancellation callback failed");
        }

        Exception? completionFailure = null;
        if (operation.Session is { } session)
        {
            try { await session.Completion.ConfigureAwait(false); }
            catch (OperationCanceledException) when (operation.Cancellation.IsCancellationRequested) { }
            catch (Exception ex) { completionFailure = ex; }
        }

        try { await operation.Observer.ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogError(ex, "Playback observer failed during cleanup"); }

        var unreportedCompletionFailure = completionFailure is not null && !ReferenceEquals(completionFailure, operation.ReportedFailure);
        if (unreportedCompletionFailure)
        {
            _logger.LogError(completionFailure, "Playback completion failed during cleanup");
        }

        Exception? restorationFailure = null;
        try
        {
            _volume.Apply(operation.Snapshot);
        }
        catch (Exception ex)
        {
            restorationFailure = ex;
            _logger.LogError(ex, "Failed to restore volume");
        }
        finally
        {
            _operation = null;
            operation.Dispose();
        }

        if (notify)
        {
            await _store.DispatchAsync(new AlarmEvent.RingingEnded(schedule, _time.GetLocalNow()), CancellationToken.None).ConfigureAwait(false);
            if (unreportedCompletionFailure && completionFailure is { } completionError)
            {
                await ReportFailureAsync(schedule, nameof(AlarmEffect.EndRinging), AlarmError.From(completionError)).ConfigureAwait(false);
            }
            if (restorationFailure is { } restoreError)
            {
                await ReportFailureAsync(schedule, nameof(AlarmEffect.EndRinging), new AlarmError.VolumeRestoreFailed(restoreError.Message)).ConfigureAwait(false);
            }
            if (cancellationFailure is { } cancelError)
            {
                await ReportFailureAsync(schedule, nameof(AlarmEffect.EndRinging), AlarmError.From(cancelError)).ConfigureAwait(false);
            }
        }
    }

    private ValueTask ReportFailureAsync(AlarmSchedule schedule, string effectName, AlarmError error) =>
        _store.DispatchAsync(new AlarmEvent.EffectFailed(schedule, effectName, error), CancellationToken.None);

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleGate)
        {
            _disposeTask ??= DisposeCoreAsync(_runCancellation, _runTask);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync(CancellationTokenSource? cancellation, Task? runTask)
    {
        Exception? cancellationFailure = null;
        if (cancellation is not null)
        {
            try { await cancellation.CancelAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                cancellationFailure = ex;
                _logger.LogError(ex, "Interpreter cancellation callback failed");
            }
        }

        try
        {
            if (runTask is not null)
            {
                await runTask.ConfigureAwait(false);
            }
        }
        finally
        {
            cancellation?.Dispose();
        }

        if (cancellationFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cancellationFailure).Throw();
        }
    }

    private sealed class PlaybackOperation(AlarmSchedule schedule, VolumeSnapshot snapshot, CancellationToken outer) : IDisposable
    {
        public AlarmSchedule Schedule { get; } = schedule;
        public VolumeSnapshot Snapshot { get; } = snapshot;
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(outer);
        public PlaybackSession? Session { get; set; }
        public Task Observer { get; set; } = Task.CompletedTask;
        public Exception? ReportedFailure { get; set; }

        public void Dispose() => Cancellation.Dispose();
    }
}
