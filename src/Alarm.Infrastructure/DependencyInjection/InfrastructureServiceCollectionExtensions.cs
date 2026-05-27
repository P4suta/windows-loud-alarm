using Alarm.Application.Ports;
using Alarm.Infrastructure.Audio;
using Alarm.Infrastructure.Time;
using Alarm.Infrastructure.Volume;
using Microsoft.Extensions.DependencyInjection;

namespace Alarm.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAlarmInfrastructure(this IServiceCollection services) => services
        .AddSingleton<FallbackAudioResolver>()
        .AddSingleton<NAudioBackend>()
        .AddSingleton<IClock, SystemClock>()
        .AddSingleton<IClockTicks, SystemClockTicks>()
        .AddSingleton<IAudioPlayer, AudioPlayer>()
        .AddSingleton<ISystemVolumeController, CoreAudioVolumeController>();
}
