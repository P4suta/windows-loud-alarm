using System.Threading.Channels;
using Alarm.Application.Effects;
using Alarm.Application.Events;
using Alarm.Application.Reducer;
using Alarm.Application.State;
using Microsoft.Extensions.Logging;
using R3;

namespace Alarm.Application.Store;

/// <summary>
/// Channel-backed reducer loop. A hosted service drives <see cref="RunAsync"/> while the
/// effect interpreter consumes <see cref="EffectReader"/> on a separate loop. Both loops
/// dispatch back through <see cref="DispatchAsync"/>, which keeps reducer execution strictly serial.
/// </summary>
internal sealed class AlarmStore : IAlarmStore, IAsyncDisposable
{
    private readonly Channel<AlarmEvent> _events =
        Channel.CreateUnbounded<AlarmEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly Channel<AlarmEffect> _effects =
        Channel.CreateUnbounded<AlarmEffect>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private readonly BehaviorSubject<AlarmState> _states = new(AlarmState.Idle.Instance);
    private readonly BehaviorSubject<int> _pending = new(0);
    private int _pendingCount;
    private readonly TimeProvider _time;
    private readonly ILogger<AlarmStore> _logger;

    public AlarmStore(TimeProvider time, ILogger<AlarmStore> logger)
    {
        _time = time;
        _logger = logger;
    }

    public AlarmState Current => _states.Value;
    public Observable<AlarmState> States => _states;

    /// <summary>
    /// In-flight event count. A value &gt;0 means at least one dispatched event hasn't
    /// finished being reduced yet. Tests use this to wait deterministically for the
    /// reducer to drain instead of polling on wall-clock time.
    ///
    /// Threading note: the counter uses Interlocked but the BehaviorSubject.OnNext
    /// is a separate step, so concurrent dispatches may emit values out of strict
    /// counter order. Harmless for "wait until 0" — 0 is reached eventually — but
    /// don't rely on observing every intermediate value.
    /// </summary>
    public Observable<int> Pending => _pending;
    public bool IsIdle => Volatile.Read(ref _pendingCount) == 0;

    internal ChannelReader<AlarmEffect> EffectReader => _effects.Reader;

    public ValueTask DispatchAsync(AlarmEvent evt, CancellationToken ct = default)
    {
        var depth = Interlocked.Increment(ref _pendingCount);
        _pending.OnNext(depth);
        return _events.Writer.WriteAsync(evt, ct);
    }

    /// <summary>Drives the reducer loop. Returns when the event channel completes or ct is cancelled.</summary>
    internal async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _events.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    var (next, effects) = AlarmReducer.Reduce(_states.Value, evt, _time.GetLocalNow());
                    if (!ReferenceEquals(next, _states.Value))
                    {
                        _logger.LogInformation("State {From} -> {To} via {Event}", _states.Value.GetType().Name, next.GetType().Name, evt.GetType().Name);
                        _states.OnNext(next);
                    }
                    foreach (var fx in effects)
                    {
                        await _effects.Writer.WriteAsync(fx, ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    var depth = Interlocked.Decrement(ref _pendingCount);
                    _pending.OnNext(depth);
                }
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        finally
        {
            _effects.Writer.TryComplete();
        }
    }

    public ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        _states.OnCompleted();
        _states.Dispose();
        _pending.OnCompleted();
        _pending.Dispose();
        return ValueTask.CompletedTask;
    }
}
