using Alarm.Application.Abstractions;
using Alarm.Application.Orchestration;
using Alarm.Application.UseCases.ArmAlarm;
using Alarm.Application.UseCases.CancelAlarm;
using Alarm.Application.UseCases.StopRinging;
using Alarm.Domain.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace Alarm.Presentation.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ArmAlarmHandler _arm;
    private readonly CancelAlarmHandler _cancel;
    private readonly StopRingingHandler _stop;
    private readonly IAudioFilePicker _picker;
    private readonly ISystemClock _clock;
    private readonly ITrayIconHost _tray;

    private readonly Timer _clockTimer;
    private DispatcherQueue? _dispatcher;
    private AlarmSchedule? _currentSchedule;

    [ObservableProperty]
    public partial TimeSpan AlarmTimeBindable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoundDisplay))]
    public partial AudioSource Sound { get; set; }

    [ObservableProperty]
    public partial bool IsArmed { get; set; }

    [ObservableProperty]
    public partial bool IsRinging { get; set; }

    [ObservableProperty]
    public partial string CurrentTimeDisplay { get; set; }

    [ObservableProperty]
    public partial string CountdownDisplay { get; set; }

    [ObservableProperty]
    public partial string FireAtDisplay { get; set; }

    [ObservableProperty]
    public partial string RingingFireAtDisplay { get; set; }

    public string SoundDisplay => Sound switch
    {
        AudioSource.UserFile uf => Path.GetFileName(uf.Path),
        AudioSource.SystemDefault => "Windows default",
        _ => "(unknown)",
    };

    public MainViewModel(
        ArmAlarmHandler arm,
        CancelAlarmHandler cancel,
        StopRingingHandler stop,
        IAudioFilePicker picker,
        RingingCoordinator coordinator,
        ISystemClock clock,
        ITrayIconHost tray)
    {
        _arm = arm;
        _cancel = cancel;
        _stop = stop;
        _picker = picker;
        _clock = clock;
        _tray = tray;

        AlarmTimeBindable = TimeSpan.FromHours(7);
        Sound = AudioSource.SystemDefault.Instance;
        CurrentTimeDisplay = "—";
        CountdownDisplay = "0:00";
        FireAtDisplay = string.Empty;
        RingingFireAtDisplay = string.Empty;

        coordinator.RingingStarted += (_, _) => RunOnUi(() =>
        {
            RingingFireAtDisplay = _currentSchedule is { } s
                ? $"{s.FireAt:HH:mm}"
                : string.Empty;
            IsRinging = true;
            IsArmed = false;
            _currentSchedule = null;
            CountdownDisplay = "0:00";
            FireAtDisplay = string.Empty;
            _tray.UpdateTooltip("Alarm ringing — click STOP");
        });
        coordinator.RingingEnded += (_, _) => RunOnUi(() =>
        {
            IsRinging = false;
            RingingFireAtDisplay = string.Empty;
            _tray.UpdateTooltip("Alarm");
        });

        _clockTimer = new Timer(_ =>
            RunOnUi(() =>
            {
                CurrentTimeDisplay = _clock.Now.ToString("yyyy-MM-dd HH:mm:ss");
                if (IsArmed) UpdateCountdown();
            }),
            state: null, dueTime: TimeSpan.Zero, period: TimeSpan.FromSeconds(1));
    }

    public void AttachDispatcher(DispatcherQueue dispatcher) => _dispatcher = dispatcher;

    private void RunOnUi(Action action)
    {
        if (_dispatcher is null) action();
        else _dispatcher.TryEnqueue(() => action());
    }

    [RelayCommand]
    private async Task ArmAsync()
    {
        var time = TimeOfDay.FromTimeSpan(AlarmTimeBindable);
        var schedule = await _arm.HandleAsync(new ArmAlarmCommand(time, Sound), CancellationToken.None);
        _currentSchedule = schedule;
        FireAtDisplay = $"fires at {schedule.FireAt:HH:mm}";
        UpdateCountdown();
        IsArmed = true;
        _tray.UpdateTooltip($"Armed: {schedule.FireAt:HH:mm}");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await _cancel.HandleAsync();
        _currentSchedule = null;
        CountdownDisplay = "0:00";
        FireAtDisplay = string.Empty;
        IsArmed = false;
        _tray.UpdateTooltip("Alarm");
    }

    [RelayCommand]
    private async Task StopRingingAsync() => await _stop.HandleAsync();

    [RelayCommand]
    private async Task PickFileAsync()
    {
        var picked = await _picker.PickAsync();
        if (picked is not null) Sound = picked;
    }

    [RelayCommand]
    private void UseSystemDefault() => Sound = AudioSource.SystemDefault.Instance;

    private void UpdateCountdown()
    {
        if (_currentSchedule is null) { CountdownDisplay = "0:00"; return; }
        var until = _currentSchedule.TimeUntil(_clock.Now);
        CountdownDisplay = FormatCountdown(until);
    }

    private static string FormatCountdown(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    public void Dispose() => _clockTimer.Dispose();
}
