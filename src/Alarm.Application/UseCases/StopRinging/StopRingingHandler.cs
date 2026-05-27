using Alarm.Application.Orchestration;
using Microsoft.Extensions.Logging;

namespace Alarm.Application.UseCases.StopRinging;

public sealed class StopRingingHandler(
    RingingCoordinator coordinator,
    ILogger<StopRingingHandler> logger)
{
    public async Task HandleAsync()
    {
        await coordinator.StopAsync().ConfigureAwait(false);
        logger.LogInformation("Ringing stopped by user");
    }
}
