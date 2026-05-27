using Alarm.Application.State;
using R3;

namespace Alarm.Application.Ports;

/// <summary>
/// Output port for surfacing alarm state to the user outside the main window (e.g. tray icon).
/// Implementations subscribe to the state stream and derive their own representation —
/// the Application layer never hands a UI string ("Armed: 07:00") to the port.
/// </summary>
public interface IAlarmStatusPresenter : IAsyncDisposable
{
    void Bind(Observable<AlarmState> states);

    event EventHandler? ShowRequested;
    event EventHandler? ExitRequested;
}
