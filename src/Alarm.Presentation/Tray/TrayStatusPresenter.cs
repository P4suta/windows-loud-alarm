using System.Runtime.InteropServices;
using Alarm.Application.Ports;
using Alarm.Application.State;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.Logging;
using R3;

namespace Alarm.Presentation.Tray;

/// <summary>
/// <see cref="IAlarmStatusPresenter"/> implementation backed by H.NotifyIcon. Subscribes
/// to the state stream and derives its own tooltip text — the Application layer never
/// supplies UI strings.
/// </summary>
internal sealed partial class TrayStatusPresenter : IAlarmStatusPresenter
{
    private readonly ILogger<TrayStatusPresenter> _logger;
    private TrayIconWithContextMenu? _icon;
    private IDisposable? _stateSub;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public TrayStatusPresenter(ILogger<TrayStatusPresenter> logger)
    {
        _logger = logger;
    }

    public void Bind(Observable<AlarmState> states)
    {
        EnsureIcon();
        _stateSub?.Dispose();
        _stateSub = states.Subscribe(state =>
        {
            if (_icon is null) return;
            _icon.UpdateToolTip(state switch
            {
                AlarmState.Idle => "Alarm",
                AlarmState.Armed a => $"Armed: {a.Schedule.FireAt:HH:mm}",
                AlarmState.Ringing => "Alarm ringing — click STOP",
                _ => "Alarm",
            });
        });
    }

    private void EnsureIcon()
    {
        if (_icon is not null) return;

        var show = new PopupMenuItem("Show", (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty));
        var exit = new PopupMenuItem("Exit", (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new TrayIconWithContextMenu
        {
            Icon = ResolveIcon(),
            ToolTip = "Alarm",
            ContextMenu = new PopupMenu { Items = { show, new PopupMenuSeparator(), exit } },
        };
        _icon.MessageWindow.MouseEventReceived += (_, e) =>
        {
            if (e.MouseEvent is MouseEvent.IconLeftDoubleClick or MouseEvent.IconLeftMouseUp)
                ShowRequested?.Invoke(this, EventArgs.Empty);
        };
        _icon.Create();
        _logger.LogInformation("Tray icon initialized");
    }

    private static nint ResolveIcon()
    {
        // user32!LoadIconW with MAKEINTRESOURCE(IDI_INFORMATION) — a built-in OS icon.
        const int idiInformation = 32516;
        return LoadIcon(nint.Zero, idiInformation);
    }

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LoadIcon(nint hInstance, nint lpIconName);

    public ValueTask DisposeAsync()
    {
        _stateSub?.Dispose();
        _stateSub = null;
        _icon?.Dispose();
        _icon = null;
        return ValueTask.CompletedTask;
    }
}
