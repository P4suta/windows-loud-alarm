using Alarm.Domain.Model;

namespace Alarm.Application.Abstractions;

/// <summary>UI-side file picker abstraction. Returns <c>null</c> if the user cancelled.</summary>
public interface IAudioFilePicker
{
    Task<AudioSource?> PickAsync();
}
