using Alarm.Application.Store;
using Microsoft.Extensions.Hosting;

namespace Alarm.Application.Runtime;

/// <summary>
/// Drives the <see cref="AlarmStore"/> reducer loop for the lifetime of the host. The store
/// channel completes on shutdown, so <see cref="AlarmStore.RunAsync"/> returns naturally.
/// </summary>
internal sealed class AlarmStoreHostedService : BackgroundService
{
    private readonly AlarmStore _store;

    public AlarmStoreHostedService(AlarmStore store)
    {
        _store = store;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        _store.RunAsync(stoppingToken);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await _store.DisposeAsync().ConfigureAwait(false);
    }
}
