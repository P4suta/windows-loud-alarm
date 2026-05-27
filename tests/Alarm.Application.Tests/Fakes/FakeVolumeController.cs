using Alarm.Application.Ports;
using Alarm.Domain.Model;

namespace Alarm.Application.Tests.Fakes;

internal sealed class FakeVolumeController : ISystemVolumeController
{
    public VolumeSnapshot CurrentSystemVolume { get; set; } = VolumeSnapshot.Of(0.5f, isMuted: false);
    public VolumeSnapshot? LastApplied { get; private set; }
    public int CaptureCount { get; private set; }
    public int ApplyCount { get; private set; }

    public VolumeSnapshot Capture()
    {
        CaptureCount++;
        return CurrentSystemVolume;
    }

    public void Apply(VolumeSnapshot snapshot)
    {
        ApplyCount++;
        LastApplied = snapshot;
        CurrentSystemVolume = snapshot;
    }
}
