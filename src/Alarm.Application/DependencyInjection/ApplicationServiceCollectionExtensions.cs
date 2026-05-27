using Alarm.Application.Orchestration;
using Alarm.Application.UseCases.ArmAlarm;
using Alarm.Application.UseCases.CancelAlarm;
using Alarm.Application.UseCases.StopRinging;
using Microsoft.Extensions.DependencyInjection;

namespace Alarm.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddAlarmApplication(this IServiceCollection services) => services
        .AddSingleton<RingingCoordinator>()
        .AddSingleton<ArmAlarmHandler>()
        .AddSingleton<CancelAlarmHandler>()
        .AddSingleton<StopRingingHandler>();
}
