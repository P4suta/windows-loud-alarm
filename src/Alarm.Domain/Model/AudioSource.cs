namespace Alarm.Domain.Model;

/// <summary>
/// Where the alarm sound is sourced from. Closed hierarchy — discriminate via pattern match.
/// </summary>
public abstract record AudioSource
{
    private AudioSource() { }

    /// <summary>An audio file selected by the user.</summary>
    public sealed record UserFile(string Path) : AudioSource;

    /// <summary>Defer to the Windows built-in alarm sound (chosen by the infrastructure adapter).</summary>
    public sealed record SystemDefault : AudioSource
    {
        public static readonly SystemDefault Instance = new();
    }
}
