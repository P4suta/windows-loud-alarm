namespace Alarm.Domain.Common;

/// <summary>
/// Closed hierarchy of expected failures across the alarm domain. Discriminate via pattern match.
/// Unexpected conditions remain exceptions; this type is the contract for things callers can recover from.
/// </summary>
public abstract record AlarmError
{
    private AlarmError() { }

    public sealed record InvalidAudioPath(string Path, string Reason) : AlarmError;
    public sealed record AudioFileMissing(string Path) : AlarmError;
    public sealed record AudioPlaybackFailed(string Detail) : AlarmError;
    public sealed record VolumeCaptureFailed(string Detail) : AlarmError;
    public sealed record VolumeRestoreFailed(string Detail) : AlarmError;
    public sealed record FilePickCancelled : AlarmError;
    public sealed record FilePickFailed(string Detail) : AlarmError;
    public sealed record Unexpected(string Detail) : AlarmError;

    public static AlarmError From(Exception ex) => new Unexpected(ex.Message);
}
