using Alarm.Application.Abstractions;
using Alarm.Domain.Model;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace Alarm.Infrastructure.Volume;

internal sealed class CoreAudioVolumeController(ILogger<CoreAudioVolumeController> logger)
    : ISystemVolumeController
{
    private static MMDevice GetEndpoint()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    public VolumeSnapshot Capture()
    {
        using var device = GetEndpoint();
        var snap = new VolumeSnapshot(device.AudioEndpointVolume.MasterVolumeLevelScalar,
                                       device.AudioEndpointVolume.Mute);
        logger.LogDebug("Captured volume: scalar={Scalar} muted={Muted}", snap.Scalar, snap.IsMuted);
        return snap;
    }

    public void Apply(VolumeSnapshot snapshot)
    {
        using var device = GetEndpoint();
        device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(snapshot.Scalar, 0f, 1f);
        device.AudioEndpointVolume.Mute = snapshot.IsMuted;
        logger.LogDebug("Applied volume: scalar={Scalar} muted={Muted}", snapshot.Scalar, snapshot.IsMuted);
    }
}
