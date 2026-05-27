using Alarm.Application.Events;
using Alarm.Application.Store;
using Microsoft.Extensions.Hosting;

namespace Alarm.Application.Runtime;

/// <summary>
/// Emits a 1 Hz <see cref="AlarmEvent.Tick"/> into the store. The reducer is responsible
/// for deciding whether the tick fires a <see cref="Effects.AlarmEffect.BeginRinging"/>.
/// </summary>
internal sealed class AlarmTickService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly IAlarmStore _store;
    private readonly TimeProvider _time;

    public AlarmTickService(IAlarmStore store, TimeProvider time)
    {
        _store = store;
        _time = time;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await _store.DispatchAsync(new AlarmEvent.Tick(_time.GetLocalNow()), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
    }
}
