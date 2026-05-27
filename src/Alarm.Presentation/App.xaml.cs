using Alarm.Application.DependencyInjection;
using Alarm.Application.Ports;
using Alarm.Application.State;
using Alarm.Application.Store;
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

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        await Host.StartAsync().ConfigureAwait(true);

        var store = Host.Services.GetRequiredService<IAlarmStore>();
        var presenter = Host.Services.GetRequiredService<IAlarmStatusPresenter>();
        presenter.ShowRequested += (_, _) => RunOnUiThread(ShowMainWindow);
        presenter.ExitRequested += (_, _) => RunOnUiThread(() => _ = StopHostAndExitAsync());

        MainWindow = Host.Services.GetRequiredService<MainWindow>();
        MainWindow.Closed += (s, e) => OnMainWindowClosed(store, s, e);
        MainWindow.Activate();

        presenter.Bind(store.States);
    }

    private void OnMainWindowClosed(IAlarmStore store, object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        if (_shuttingDown) return;

        // タイマー稼働中(Armed/Ringing)は不発防止のためトレイへ最小化、未設定(Idle)時は普通にアプリ終了。
        if (store.Current is AlarmState.Idle)
        {
            args.Handled = true;
            _ = StopHostAndExitAsync();
        }
        else if (sender is MainWindow window)
        {
            args.Handled = true;
            window.AppWindow?.Hide();
        }
    }

    private void ShowMainWindow()
    {
        if (MainWindow is not MainWindow mw) return;
        mw.AppWindow?.Show();
        mw.Activate();
    }

    private void RunOnUiThread(Action action)
    {
        if (MainWindow is { DispatcherQueue: var dq })
            dq.TryEnqueue(() => action());
        else
            action();
    }

    private async Task StopHostAndExitAsync()
    {
        _shuttingDown = true;
        try
        {
            await Host.StopAsync().ConfigureAwait(true);
            Host.Dispose();
        }
        catch (Exception ex)
        {
            Host.Services.GetService<ILogger<App>>()?.LogError(ex, "Shutdown failed");
        }
        Exit();
    }
}
