using Alarm.Application.Abstractions;
using Alarm.Domain.Events;
using Alarm.Domain.Model;
using Microsoft.Extensions.Logging;

namespace Alarm.Infrastructure.Scheduling;

/// <summary>
/// Polls <see cref="ISystemClock"/> at second granularity and fires <see cref="Triggered"/>
/// once the armed schedule's fire-time is reached. Re-arming replaces the current schedule.
/// </summary>
internal sealed class PeriodicTimerAlarmScheduler(
    ISystemClock clock,
    ILogger<PeriodicTimerAlarmScheduler> logger) : IAlarmScheduler, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _runCts;
    private Task? _runner;

    public AlarmSchedule? Current { get; private set; }

    public event Func<AlarmTriggered, Task>? Triggered;

    public async Task ArmAsync(AlarmSchedule schedule, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StopRunnerUnsafeAsync().ConfigureAwait(false);

            Current = schedule;
            _runCts = new CancellationTokenSource();
            _runner = Task.Run(() => RunAsync(schedule, _runCts.Token), CancellationToken.None);
            logger.LogInformation("Scheduler armed for {FireAt}", schedule.FireAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CancelAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopRunnerUnsafeAsync().ConfigureAwait(false);
            Current = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopRunnerUnsafeAsync()
    {
        if (_runCts is null) return;
        await _runCts.CancelAsync().ConfigureAwait(false);
        try { if (_runner is not null) await _runner.ConfigureAwait(false); }
        catch (OperationCanceledException) { /* expected */ }
        _runCts.Dispose();
        _runCts = null;
        _runner = null;
    }

    private async Task RunAsync(AlarmSchedule schedule, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var now = clock.Now;
                if (!schedule.IsDue(now)) continue;

                logger.LogInformation("Alarm due at {Now} (fireAt={FireAt})", now, schedule.FireAt);
                Current = null;

                var handler = Triggered;
                if (handler is not null)
                {
                    var evt = new AlarmTriggered(schedule, now);
                    foreach (var h in handler.GetInvocationList().Cast<Func<AlarmTriggered, Task>>())
                    {
                        try { await h(evt).ConfigureAwait(false); }
                        catch (Exception ex) { logger.LogError(ex, "Triggered handler threw"); }
                    }
                }
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // expected on cancel
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CancelAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
