using Alarm.Application.Store;
using Microsoft.Extensions.Hosting;

namespace Alarm.Application.Runtime;

/// <summary>Drives the <see cref="EffectInterpreter"/> effect loop for the lifetime of the host.</summary>
internal sealed class EffectInterpreterHostedService : BackgroundService
{
    private readonly EffectInterpreter _interpreter;

    public EffectInterpreterHostedService(EffectInterpreter interpreter)
    {
        _interpreter = interpreter;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        _interpreter.RunAsync(stoppingToken);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await _interpreter.DisposeAsync().ConfigureAwait(false);
    }
}
