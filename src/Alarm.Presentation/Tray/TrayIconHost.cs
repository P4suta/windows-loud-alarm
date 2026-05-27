using Alarm.Application.Abstractions;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.Logging;

namespace Alarm.Presentation.Tray;

internal sealed partial class TrayIconHost(ILogger<TrayIconHost> logger) : ITrayIconHost
{
    private TrayIconWithContextMenu? _icon;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public void Initialize()
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
        logger.LogInformation("Tray icon initialized");
    }

    public void UpdateTooltip(string text)
    {
        if (_icon is null) return;
        _icon.UpdateToolTip(text);
    }

    private static nint ResolveIcon()
    {
        // user32!LoadIconW with MAKEINTRESOURCE(IDI_INFORMATION) — a built-in OS icon.
        // Avoids shipping an .ico file while still showing a recognizable tray glyph.
        const int idiInformation = 32516; // user32 IDI_INFORMATION resource id
        return LoadIcon(nint.Zero, idiInformation);
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "LoadIconW", SetLastError = true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static partial nint LoadIcon(nint hInstance, nint lpIconName);

    public ValueTask DisposeAsync()
    {
        _icon?.Dispose();
        _icon = null;
        return ValueTask.CompletedTask;
    }
}
