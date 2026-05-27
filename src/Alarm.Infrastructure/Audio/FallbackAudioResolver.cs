namespace Alarm.Infrastructure.Audio;

/// <summary>
/// Resolves a "Windows default" alarm sound by probing well-known files under <c>%WINDIR%\Media</c>.
/// </summary>
internal sealed class FallbackAudioResolver
{
    private static readonly string[] Candidates =
    [
        "Alarm01.wav",
        "Alarm02.wav",
        "Alarm03.wav",
        "Ring01.wav",
        "Ring02.wav",
        "Ring03.wav",
        "ringout.wav",
        "Windows Background.wav",
    ];

    public string? TryResolve()
    {
        var mediaDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
        foreach (var name in Candidates)
        {
            var p = Path.Combine(mediaDir, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
