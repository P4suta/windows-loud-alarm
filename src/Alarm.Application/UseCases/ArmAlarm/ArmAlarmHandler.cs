using Alarm.Application.Abstractions;
using Alarm.Domain.Model;
using Microsoft.Extensions.Logging;

namespace Alarm.Application.UseCases.ArmAlarm;

public sealed class ArmAlarmHandler(
    IAlarmScheduler scheduler,
    ISystemClock clock,
    ILogger<ArmAlarmHandler> logger)
{
    public async Task<AlarmSchedule> HandleAsync(ArmAlarmCommand command, CancellationToken ct)
    {
        var schedule = AlarmSchedule.Create(command.Time, command.Sound, clock.Now);
        await scheduler.ArmAsync(schedule, ct).ConfigureAwait(false);
        logger.LogInformation("Armed alarm for {FireAt} (in {Delay})", schedule.FireAt, schedule.TimeUntil(clock.Now));
        return schedule;
    }
}
