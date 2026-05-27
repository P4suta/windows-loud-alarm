namespace Alarm.Domain.Model;

/// <summary>
/// Captured master-volume state of the default render endpoint at a point in time.
/// <see cref="Scalar"/> is a linear 0.0–1.0 value matching Core Audio's MasterVolumeLevelScalar.
/// </summary>
public readonly record struct VolumeSnapshot
{
    public float Scalar { get; }
    public bool IsMuted { get; }

    private VolumeSnapshot(float scalar, bool isMuted)
    {
        Scalar = Math.Clamp(scalar, 0f, 1f);
        IsMuted = isMuted;
    }

    public static VolumeSnapshot Of(float scalar, bool isMuted) => new(scalar, isMuted);

    public static VolumeSnapshot Maximum { get; } = new(1.0f, isMuted: false);
}
