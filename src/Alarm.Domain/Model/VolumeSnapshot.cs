namespace Alarm.Domain.Model;

/// <summary>
/// Captured master-volume state of the default render endpoint at a point in time.
/// <see cref="Scalar"/> is a linear 0.0–1.0 value matching Core Audio's MasterVolumeLevelScalar.
/// </summary>
public readonly record struct VolumeSnapshot(float Scalar, bool IsMuted)
{
    public static VolumeSnapshot Maximum { get; } = new(1.0f, IsMuted: false);
}
