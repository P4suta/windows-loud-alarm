using Alarm.Application.Abstractions;
using Alarm.Domain.Model;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Alarm.Presentation.Dialogs;

internal sealed class WinUIFilePicker(Func<Window?> windowAccessor) : IAudioFilePicker
{
    public async Task<AudioSource?> PickAsync()
    {
        var window = windowAccessor()
            ?? throw new InvalidOperationException("Main window not available — cannot open file picker.");

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
        return file is null ? null : new AudioSource.UserFile(file.Path);
    }
}
