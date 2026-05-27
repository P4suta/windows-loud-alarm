using Alarm.Application.Ports;
using Alarm.Domain.Common;
using Alarm.Domain.Model;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Alarm.Presentation.Dialogs;

internal sealed class WinUIFilePicker : IAudioFilePicker
{
    private readonly Func<Window?> _windowAccessor;

    public WinUIFilePicker(Func<Window?> windowAccessor)
    {
        _windowAccessor = windowAccessor;
    }

    public async Task<Result<AudioSource.UserFile, AlarmError>> PickAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var window = _windowAccessor();
        if (window is null)
            return Result.Err<AudioSource.UserFile, AlarmError>(new AlarmError.FilePickFailed("Main window not available."));

        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
        };
        picker.FileTypeFilter.Add(".wav");
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".m4a");
        picker.FileTypeFilter.Add(".aac");
        picker.FileTypeFilter.Add(".flac");
        picker.FileTypeFilter.Add(".ogg");

        // WinUI 3 requires explicit window-handle association for pickers.
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var file = await picker.PickSingleFileAsync();
        return file is null
            ? Result.Err<AudioSource.UserFile, AlarmError>(new AlarmError.FilePickCancelled())
            : AudioSource.UserFile.Of(file.Path);
    }
}
