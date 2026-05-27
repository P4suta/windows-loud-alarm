namespace Alarm.Application.Abstractions;

/// <summary>System-tray adapter. Owns the notification-area icon lifecycle and exposes user intents as events.</summary>
public interface ITrayIconHost : IAsyncDisposable
{
    void Initialize();
    void UpdateTooltip(string text);

    /// <summary>The user asked to bring the main window back from the tray.</summary>
    event EventHandler? ShowRequested;

    /// <summary>The user asked to fully exit the application.</summary>
    event EventHandler? ExitRequested;
}
