using Alarm.Application.Abstractions;
using Alarm.Infrastructure.Audio;
using Alarm.Infrastructure.Scheduling;
using Alarm.Infrastructure.Time;
using Alarm.Infrastructure.Volume;
using Microsoft.Extensions.DependencyInjection;

namespace Alarm.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAlarmInfrastructure(this IServiceCollection services) => services
        .AddSingleton<FallbackAudioResolver>()
        .AddSingleton<IAudioPlayer, NAudioPlayer>()
        .AddSingleton<ISystemVolumeController, CoreAudioVolumeController>()
        .AddSingleton<IAlarmScheduler, PeriodicTimerAlarmScheduler>()
        .AddSingleton<ISystemClock, SystemClock>();
}
