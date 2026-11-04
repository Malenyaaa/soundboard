namespace Soundboard.Core.Audio;

/// <summary>
/// Lock-free ring buffer for exactly one writer thread and one reader thread (capture thread
/// writes, render thread reads). Fixed size; neither side allocates or blocks.
/// </summary>
public sealed class FloatRingBuffer
{
    private readonly float[] _data;
    private long _written; // total samples ever written (writer owns)
    private long _read;    // total samples ever read (reader owns)
    private long _dropped;

    public FloatRingBuffer(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _data = new float[capacity];
    }

    public int Capacity => _data.Length;

    /// <summary>Samples ready to read (reader view).</summary>
    public int Available => (int)(Volatile.Read(ref _written) - Volatile.Read(ref _read));

    /// <summary>Samples the writer could not store because the buffer was full.</summary>
    public long Dropped => Volatile.Read(ref _dropped);

    /// <summary>Writer: stores as much as fits; the rest is dropped and counted.</summary>
    public int Write(ReadOnlySpan<float> samples)
    {
        long w = _written;
        long r = Volatile.Read(ref _read);
        int free = _data.Length - (int)(w - r);
        int n = Math.Min(free, samples.Length);
        if (n < samples.Length) Volatile.Write(ref _dropped, _dropped + samples.Length - n);
        int pos = (int)(w % _data.Length);
        int first = Math.Min(n, _data.Length - pos);
        samples[..first].CopyTo(_data.AsSpan(pos));
        samples.Slice(first, n - first).CopyTo(_data.AsSpan(0));
        Volatile.Write(ref _written, w + n);
        return n;
    }

    /// <summary>Writer: stores <paramref name="count"/> zeros (silent packets).</summary>
    public int WriteSilence(int count)
    {
        long w = _written;
        long r = Volatile.Read(ref _read);
        int free = _data.Length - (int)(w - r);
        int n = Math.Min(free, count);
        if (n < count) Volatile.Write(ref _dropped, _dropped + count - n);
        int pos = (int)(w % _data.Length);
        int first = Math.Min(n, _data.Length - pos);
        Array.Clear(_data, pos, first);
        Array.Clear(_data, 0, n - first);
        Volatile.Write(ref _written, w + n);
        return n;
    }

    /// <summary>Reader: copies up to <paramref name="dest"/>.Length samples; returns the count.</summary>
    public int Read(Span<float> dest)
    {
        long r = _read;
        long w = Volatile.Read(ref _written);
        int n = Math.Min((int)(w - r), dest.Length);
        int pos = (int)(r % _data.Length);
        int first = Math.Min(n, _data.Length - pos);
        _data.AsSpan(pos, first).CopyTo(dest);
        _data.AsSpan(0, n - first).CopyTo(dest[first..]);
        Volatile.Write(ref _read, r + n);
        return n;
    }

    /// <summary>Reader: throws away <paramref name="count"/> samples (to catch up after drift).</summary>
    public int Skip(int count)
    {
        long r = _read;
        long w = Volatile.Read(ref _written);
        int n = Math.Min((int)(w - r), count);
        Volatile.Write(ref _read, r + n);
        return n;
    }
}
