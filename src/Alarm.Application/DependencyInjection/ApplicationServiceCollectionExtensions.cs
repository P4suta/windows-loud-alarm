using Alarm.Application.Runtime;
using Alarm.Application.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Alarm.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the alarm state machine: store, effect interpreter, and the hosted services
    /// that drive both loops plus the 1 Hz tick. <see cref="TimeProvider"/> falls back to the
    /// system clock if no other registration has provided one.
    /// </summary>
    public static IServiceCollection AddAlarmApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<AlarmStore>();
        services.AddSingleton<IAlarmStore>(sp => sp.GetRequiredService<AlarmStore>());
        services.AddSingleton<EffectInterpreter>();

        services.AddHostedService<AlarmStoreHostedService>();
        services.AddHostedService<EffectInterpreterHostedService>();
        services.AddHostedService<AlarmTickService>();

        return services;
    }
}
