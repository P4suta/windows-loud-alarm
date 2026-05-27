using Alarm.Application.Ports;
using Alarm.Presentation.Dialogs;
using Alarm.Presentation.Tray;
using Alarm.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Alarm.Presentation.Composition;

public static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddAlarmPresentation(this IServiceCollection services) => services
        .AddSingleton<IAlarmStatusPresenter, TrayStatusPresenter>()
        .AddSingleton<IAudioFilePicker>(_ => new WinUIFilePicker(() => App.Current.MainWindow))
        .AddSingleton<MainViewModel>()
        .AddSingleton<MainWindow>();
}
