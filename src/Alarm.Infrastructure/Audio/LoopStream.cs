using NAudio.Wave;

namespace Alarm.Infrastructure.Audio;

/// <summary>
/// Wraps a finite <see cref="WaveStream"/> and rewinds to position 0 each time the source ends,
/// producing an endless looped read sequence. Disposes the underlying source on dispose.
/// </summary>
internal sealed class LoopStream(WaveStream source) : WaveStream
{
    public override WaveFormat WaveFormat => source.WaveFormat;
    public override long Length => source.Length;

    public override long Position
    {
        get => source.Position;
        set => source.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = source.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                if (source.Position == 0) break; // empty source — avoid hot spin
                source.Position = 0;
                continue;
            }
            total += read;
        }
        return total;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) source.Dispose();
        base.Dispose(disposing);
    }
}
