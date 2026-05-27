using System.Diagnostics;
using Alarm.Application.Events;
using Alarm.Application.Ports;
using Alarm.Application.State;
using Alarm.Application.Store;
using Alarm.Domain.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using R3;

namespace Alarm.Presentation.ViewModels;

/// <summary>
/// Projection of <see cref="AlarmState"/> into the bindable surface consumed by MainWindow.xaml.
/// The ViewModel never holds mutable lifecycle state itself — IsArmed/IsRinging/CountdownDisplay
/// are all derived from the store stream + clock ticks.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAlarmStore _store;
    private readonly IAudioFilePicker _picker;
    private readonly IClock _clock;
    private readonly DispatcherQueue _dispatcher;
    private readonly IDisposable _stateSub;
    private readonly IDisposable _tickSub;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlarmTimeDisplay))]
    public partial TimeSpan AlarmTimeBindable { get; set; }

    public string AlarmTimeDisplay => $"{AlarmTimeBindable:hh\\:mm}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoundDisplay))]
    public partial AudioSource Sound { get; set; }

    [ObservableProperty]
    public partial bool IsArmed { get; private set; }

    [ObservableProperty]
    public partial bool IsRinging { get; private set; }

    [ObservableProperty]
    public partial string CurrentTimeDisplay { get; private set; }

    [ObservableProperty]
    public partial string CountdownDisplay { get; private set; }

    [ObservableProperty]
    public partial string FireAtDisplay { get; private set; }

    [ObservableProperty]
    public partial string RingingFireAtDisplay { get; private set; }

    public string SoundDisplay => Sound switch
    {
        AudioSource.UserFile uf => Path.GetFileName(uf.Path),
        AudioSource.SystemDefault => "Windows default",
        _ => "(unknown)",
    };

    public MainViewModel(IAlarmStore store, IAudioFilePicker picker, IClock clock, IClockTicks ticks)
    {
        _store = store;
        _picker = picker;
        _clock = clock;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("MainViewModel must be constructed on a UI thread.");

        AlarmTimeBindable = TimeSpan.FromHours(7);
        Sound = AudioSource.SystemDefault.Instance;
        CurrentTimeDisplay = "—";
        CountdownDisplay = "0:00";
        FireAtDisplay = string.Empty;
        RingingFireAtDisplay = string.Empty;

        _stateSub = store.States
            .DistinctUntilChanged()
            .Subscribe(this, static (s, self) => self.RunOnUi(() => self.ApplyState(s)));

        _tickSub = ticks.Stream
            .Subscribe(this, static (now, self) => self.RunOnUi(() => self.ApplyTick(now)));
    }

    private void ApplyState(AlarmState state)
    {
        var (isArmed, isRinging, fireAt, ringingFireAt, countdown) = state switch
        {
            AlarmState.Idle =>
                (false, false, string.Empty, string.Empty, "0:00"),
            AlarmState.Armed a =>
                (true, false, $"fires at {a.Schedule.FireAt:HH:mm}", string.Empty,
                 FormatCountdown(a.Schedule.TimeUntil(_clock.Now))),
            AlarmState.Ringing r =>
                (false, true, string.Empty, $"{r.Schedule.FireAt:HH:mm}", "0:00"),
            _ => throw new UnreachableException(),
        };
        IsArmed = isArmed;
        IsRinging = isRinging;
        FireAtDisplay = fireAt;
        RingingFireAtDisplay = ringingFireAt;
        CountdownDisplay = countdown;
    }

    private void ApplyTick(DateTimeOffset now)
    {
        CurrentTimeDisplay = now.ToString("yyyy-MM-dd HH:mm:ss");
        if (_store.Current is AlarmState.Armed armed)
            CountdownDisplay = FormatCountdown(armed.Schedule.TimeUntil(now));
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }

    [RelayCommand]
    private async Task ArmAsync()
    {
        var time = TimeOfDay.FromTimeSpan(AlarmTimeBindable);
        await _store.DispatchAsync(new AlarmEvent.ArmRequested(time, Sound)).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task CancelAsync() =>
        await _store.DispatchAsync(AlarmEvent.CancelRequested.Instance).ConfigureAwait(false);

    [RelayCommand]
    private async Task StopRingingAsync() =>
        await _store.DispatchAsync(AlarmEvent.StopRingingRequested.Instance).ConfigureAwait(false);

    [RelayCommand]
    private async Task PickFileAsync()
    {
        var result = await _picker.PickAsync(CancellationToken.None).ConfigureAwait(true);
        if (result.IsOk) Sound = result.Value;
    }

    [RelayCommand]
    private void UseSystemDefault() => Sound = AudioSource.SystemDefault.Instance;

    private static string FormatCountdown(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    public void Dispose()
    {
        _stateSub.Dispose();
        _tickSub.Dispose();
    }
}
