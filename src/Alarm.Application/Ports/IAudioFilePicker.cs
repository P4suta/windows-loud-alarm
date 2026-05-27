using Alarm.Domain.Common;
using Alarm.Domain.Model;

namespace Alarm.Application.Ports;

/// <summary>UI-side file picker abstraction. Returns the chosen file as a domain value, or an error.</summary>
public interface IAudioFilePicker
{
    Task<Result<AudioSource.UserFile, AlarmError>> PickAsync(CancellationToken ct);
}
