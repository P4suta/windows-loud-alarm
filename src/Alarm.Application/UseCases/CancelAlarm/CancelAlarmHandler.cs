using Alarm.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Alarm.Application.UseCases.CancelAlarm;

public sealed class CancelAlarmHandler(
    IAlarmScheduler scheduler,
    ILogger<CancelAlarmHandler> logger)
{
    public async Task HandleAsync()
    {
        await scheduler.CancelAsync().ConfigureAwait(false);
        logger.LogInformation("Alarm cancelled");
    }
}
