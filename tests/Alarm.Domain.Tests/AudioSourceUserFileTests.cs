using Alarm.Domain.Common;
using Alarm.Domain.Model;
using Shouldly;
using Xunit;

namespace Alarm.Domain.Tests;

public class AudioSourceUserFileTests
{
    [Theory]
    [InlineData(@"C:\music\alarm.wav")]
    [InlineData(@"D:\subdir\file.mp3")]
    public void Of_AcceptsAbsolutePaths(string path)
    {
        var result = AudioSource.UserFile.Of(path);

        result.IsOk.ShouldBeTrue();
        result.Value.Path.ShouldBe(path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_RejectsEmptyOrWhitespace(string path)
    {
        var result = AudioSource.UserFile.Of(path);

        result.IsOk.ShouldBeFalse();
        result.Error.ShouldBeOfType<AlarmError.InvalidAudioPath>();
        ((AlarmError.InvalidAudioPath)result.Error).Reason.ShouldBe("empty");
    }

    [Theory]
    [InlineData("alarm.wav")]
    [InlineData(@"relative\path.wav")]
    public void Of_RejectsRelativePaths(string path)
    {
        var result = AudioSource.UserFile.Of(path);

        result.IsOk.ShouldBeFalse();
        result.Error.ShouldBeOfType<AlarmError.InvalidAudioPath>();
        ((AlarmError.InvalidAudioPath)result.Error).Reason.ShouldBe("not absolute");
    }
}
