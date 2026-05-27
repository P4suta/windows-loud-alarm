using Alarm.Domain.Common;

namespace Alarm.Domain.Model;

/// <summary>
/// Where the alarm sound is sourced from. Closed hierarchy — discriminate via pattern match.
/// </summary>
public abstract record AudioSource
{
    private AudioSource() { }

    /// <summary>An audio file selected by the user. Constructed only through <see cref="Of(string)"/>.</summary>
    public sealed record UserFile : AudioSource
    {
        public string Path { get; }
        private UserFile(string path)
        {
            Path = path;
        }

        public static Result<UserFile, AlarmError> Of(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? Result.Err<UserFile, AlarmError>(new AlarmError.InvalidAudioPath(path ?? string.Empty, "empty"))
                : !System.IO.Path.IsPathRooted(path)
                ? Result.Err<UserFile, AlarmError>(new AlarmError.InvalidAudioPath(path, "not absolute"))
                : Result.Ok<UserFile, AlarmError>(new UserFile(path));
        }
    }

    /// <summary>Defer to the Windows built-in alarm sound (chosen by the infrastructure adapter).</summary>
    public sealed record SystemDefault : AudioSource
    {
        public static readonly SystemDefault Instance = new();
        private SystemDefault() { }
    }
}
