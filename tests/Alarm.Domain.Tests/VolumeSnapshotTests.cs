using Alarm.Domain.Model;
using Shouldly;
using Xunit;

namespace Alarm.Domain.Tests;

public class VolumeSnapshotTests
{
    [Theory]
    [InlineData(0.0f, 0.0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1.0f, 1.0f)]
    [InlineData(-0.1f, 0.0f)]   // clamp low
    [InlineData(1.5f, 1.0f)]    // clamp high
    public void Of_ClampsScalarToZeroOne(float input, float expected)
    {
        var snap = VolumeSnapshot.Of(input, isMuted: false);

        snap.Scalar.ShouldBe(expected);
    }

    [Fact]
    public void Maximum_IsFullVolumeAndUnmuted()
    {
        VolumeSnapshot.Maximum.Scalar.ShouldBe(1.0f);
        VolumeSnapshot.Maximum.IsMuted.ShouldBeFalse();
    }
}
