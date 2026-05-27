using Alarm.Application.Abstractions;
using Alarm.Application.DependencyInjection;
using Alarm.Application.Orchestration;
using Alarm.Infrastructure.DependencyInjection;
using Alarm.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Alarm.Presentation;

public partial class App : Microsoft.UI.Xaml.Application
{
    public static new App Current => (App)Microsoft.UI.Xaml.Application.Current;
    public IHost Host { get; }
    public Microsoft.UI.Xaml.Window? MainWindow { get; private set; }

    private bool _shuttingDown;

    public App()
    {
        InitializeComponent();

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services
            .AddAlarmApplication()
            .AddAlarmInfrastructure()
            .AddAlarmPresentation();
        Host = builder.Build();

        UnhandledException += (_, e) =>
            Host.Services.GetService<ILogger<App>>()?.LogCritical(e.Exception, "Unhandled XAML exception");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Host.Services.GetService<ILogger<App>>()?.LogCritical(e.ExceptionObject as Exception, "Unhandled AppDomain exception");
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var coordinator = Host.Services.GetRequiredService<RingingCoordinator>();
        coordinator.Attach();

        var trayHost = Host.Services.GetRequiredService<ITrayIconHost>();
        trayHost.ShowRequested += (_, _) => MainWindow?.Activate();
        trayHost.ExitRequested += OnExitRequested;

        MainWindow = Host.Services.GetRequiredService<MainWindow>();
        MainWindow.Closed += OnMainWindowClosed;
        MainWindow.Activate();

        trayHost.Initialize();
    }

    private void OnExitRequested(object? sender, EventArgs e) => _ = ShutdownGuardedAsync();

    private void OnMainWindowClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        if (_shuttingDown) return;
        args.Handled = true;
        MainWindow?.AppWindow?.Hide();
    }

    private async Task ShutdownGuardedAsync()
    {
        try
        {
            await ShutdownAsync();
        }
        catch (Exception ex)
        {
            Host.Services.GetService<ILogger<App>>()?.LogError(ex, "Shutdown failed");
        }
    }

    private async Task ShutdownAsync()
    {
        _shuttingDown = true;
        var coordinator = Host.Services.GetRequiredService<RingingCoordinator>();
        await coordinator.DisposeAsync();
        var tray = Host.Services.GetRequiredService<ITrayIconHost>();
        await tray.DisposeAsync();
        await Host.StopAsync();
        Host.Dispose();
        MainWindow?.Close();
        Exit();
    }
}
