using Alarm.Application.Ports;
using Alarm.Domain.Model;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace Alarm.Infrastructure.Volume;

/// <summary>
/// Master-volume capture/restore via Core Audio. The MMDevice handle is cached and
/// re-resolved on demand so endpoint changes (headphones plugged in mid-ringing, etc.)
/// don't permanently break us.
/// </summary>
internal sealed class CoreAudioVolumeController : ISystemVolumeController, IDisposable
{
    private readonly ILogger<CoreAudioVolumeController> _logger;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Lock _gate = new();
    private MMDevice? _device;

    public CoreAudioVolumeController(ILogger<CoreAudioVolumeController> logger)
    {
        _logger = logger;
    }

    public VolumeSnapshot Capture()
    {
        lock (_gate)
        {
            var dev = AcquireDevice();
            var vol = dev.AudioEndpointVolume;
            var snap = VolumeSnapshot.Of(vol.MasterVolumeLevelScalar, vol.Mute);
            _logger.LogDebug("Captured volume: scalar={Scalar} muted={Muted}", snap.Scalar, snap.IsMuted);
            return snap;
        }
    }

    public void Apply(VolumeSnapshot snapshot)
    {
        lock (_gate)
        {
            var dev = AcquireDevice();
            dev.AudioEndpointVolume.MasterVolumeLevelScalar = snapshot.Scalar;
            dev.AudioEndpointVolume.Mute = snapshot.IsMuted;
            _logger.LogDebug("Applied volume: scalar={Scalar} muted={Muted}", snapshot.Scalar, snapshot.IsMuted);
        }
    }

    private MMDevice AcquireDevice()
    {
        if (_device is not null)
        {
            try
            {
                _ = _device.AudioEndpointVolume.MasterVolumeLevelScalar; // health probe
                return _device;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cached audio endpoint went stale — re-resolving");
                _device.Dispose();
                _device = null;
            }
        }
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return _device;
    }

    public void Dispose()
    {
        _device?.Dispose();
        _enumerator.Dispose();
    }
}
