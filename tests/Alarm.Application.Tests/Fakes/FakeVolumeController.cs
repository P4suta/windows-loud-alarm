using Alarm.Application.Ports;
using Alarm.Domain.Model;

namespace Alarm.Application.Tests.Fakes;

internal sealed class FakeVolumeController : ISystemVolumeController
{
    private readonly TaskCompletionSource _restored = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private VolumeSnapshot? _captured;

    public VolumeSnapshot CurrentSystemVolume { get; set; } = VolumeSnapshot.Of(0.5f, isMuted: false);
    public VolumeSnapshot? LastApplied { get; private set; }
    public int CaptureCount { get; private set; }
    public int ApplyCount { get; private set; }
    public Task Restored => _restored.Task;

    public VolumeSnapshot Capture()
    {
        CaptureCount++;
        _captured = CurrentSystemVolume;
        return CurrentSystemVolume;
    }

    public void Apply(VolumeSnapshot snapshot)
    {
        ApplyCount++;
        LastApplied = snapshot;
        CurrentSystemVolume = snapshot;
        if (_captured == snapshot)
        {
            _restored.TrySetResult();
        }
    }
}
