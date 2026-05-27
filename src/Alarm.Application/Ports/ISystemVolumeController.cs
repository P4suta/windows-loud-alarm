using Alarm.Domain.Model;

namespace Alarm.Application.Ports;

/// <summary>Read &amp; mutate the master volume of the default render endpoint.</summary>
public interface ISystemVolumeController
{
    VolumeSnapshot Capture();
    void Apply(VolumeSnapshot snapshot);
}
