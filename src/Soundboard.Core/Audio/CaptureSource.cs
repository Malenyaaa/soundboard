using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Soundboard.Core.Audio;

/// <summary>Buffer size choice for the microphone path ("Puffer").</summary>
public enum BufferSize
{
    Small,
    Medium,
    Large,
}

public static class BufferSizes
{
    /// <summary>Target latency per stage in milliseconds.</summary>
    public static int Milliseconds(BufferSize b) => b switch
    {
        BufferSize.Small => 20,
        BufferSize.Large => 80,
        _ => 40,
    };

    public static string Display(BufferSize b) => b switch
    {
        BufferSize.Small => "klein (20 ms)",
        BufferSize.Large => "groß (80 ms)",
        _ => "mittel (40 ms)",
    };
}

/// <summary>A running microphone: delivers float samples into a ring buffer.</summary>
public interface ICaptureInput : IDisposable
{
    string DeviceId { get; }

    /// <summary>Rate and channel count of the samples in <see cref="Buffer"/> (the device mix format).</summary>
    TargetFormat Format { get; }

    FloatRingBuffer Buffer { get; }

    void Start();

    void Stop();

    /// <summary>Capture stopped on its own (device removed, error).</summary>
    event EventHandler<Exception?>? Stopped;
}

/// <summary>
/// Microphone capture: WASAPI shared mode, event driven, endpoint opened by ID, in the
/// device's own mix format (no format change at the device, no exclusive mode). Runs its own
/// thread; after start it neither allocates nor takes locks.
/// </summary>
public sealed class CaptureSource : ICaptureInput
{
    private readonly MMDevice _device;
    private readonly WaveFormat _mix;
    private readonly int _bufferMs;
    private readonly SynchronizationContext? _context;
    private Thread? _thread;
    private volatile bool _stop;

    /// <summary>Takes ownership of <paramref name="device"/>.</summary>
    public CaptureSource(MMDevice device, int bufferMs)
    {
        _device = device;
        DeviceId = device.ID;
        _bufferMs = Math.Max(10, bufferMs);
        _context = SynchronizationContext.Current;
        using (var client = device.AudioClient) _mix = client.MixFormat;
        Format = new TargetFormat(_mix.SampleRate, _mix.Channels);
        Buffer = new FloatRingBuffer(_mix.SampleRate * _mix.Channels * 2); // 2 s
    }

    public string DeviceId { get; }

    public TargetFormat Format { get; }

    public FloatRingBuffer Buffer { get; }

    public event EventHandler<Exception?>? Stopped;

    public void Start()
    {
        if (_thread is not null) return;
        _stop = false;
        _thread = new Thread(Run) { IsBackground = true, Name = "Mikrofon", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        var t = _thread;
        _thread = null;
        if (t is not null && t != Thread.CurrentThread) t.Join(1000);
    }

    private void Run()
    {
        Exception? error = null;
        AudioClient? client = null;
        try
        {
            client = _device.AudioClient;
            long duration = _bufferMs * 10_000L; // 100-ns units
            client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.EventCallback, duration, 0, _mix, Guid.Empty);
            using var ready = new EventWaitHandle(false, EventResetMode.AutoReset);
            client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
            var capture = client.AudioCaptureClient;
            var converter = new SampleConverter(_mix, client.BufferSize);
            client.Start();
            while (!_stop)
            {
                if (!ready.WaitOne(200)) continue;
                int packet = capture.GetNextPacketSize();
                while (packet > 0 && !_stop)
                {
                    IntPtr data = capture.GetBuffer(out int frames, out AudioClientBufferFlags flags);
                    if ((flags & AudioClientBufferFlags.Silent) != 0) Buffer.WriteSilence(frames * _mix.Channels);
                    else converter.Convert(data, frames, Buffer);
                    capture.ReleaseBuffer(frames);
                    packet = capture.GetNextPacketSize();
                }
            }
            client.Stop();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            error = ex;
        }
        finally
        {
            client?.Dispose();
        }

        if (!_stop || error is not null)
        {
            if (_context is not null) _context.Post(_ => Stopped?.Invoke(this, error), null);
            else Stopped?.Invoke(this, error);
        }
    }

    public void Dispose()
    {
        Stop();
        _device.Dispose();
    }
}

/// <summary>
/// Converts one WASAPI packet (float32, int16, int24 or int32 PCM) to float and stores it.
/// All scratch memory is allocated up front.
/// </summary>
internal sealed class SampleConverter
{
    private readonly int _channels;
    private readonly int _bits;
    private readonly bool _float;
    private float[] _floats;
    private byte[] _bytes;

    public SampleConverter(WaveFormat format, int maxFrames)
    {
        _channels = format.Channels;
        _bits = format.BitsPerSample;
        var info = DeviceCatalog.ToInfo(format);
        _float = info.IsFloat;
        int samples = Math.Max(1, maxFrames) * _channels;
        _floats = new float[samples];
        _bytes = new byte[samples * Math.Max(1, _bits / 8)];
        if (!_float && _bits is not (16 or 24 or 32))
            throw new NotSupportedException($"Mikrofon-Format mit {_bits} bit wird nicht unterstützt.");
    }

    public void Convert(IntPtr data, int frames, FloatRingBuffer target)
    {
        int samples = frames * _channels;
        if (samples > _floats.Length)
        {
            // Only if Windows hands over more than the negotiated buffer (should not happen).
            _floats = new float[samples];
            _bytes = new byte[samples * (_bits / 8)];
        }
        int byteCount = samples * (_bits / 8);
        Marshal.Copy(data, _bytes, 0, byteCount);
        ConvertBytes(_bytes.AsSpan(0, byteCount), _floats.AsSpan(0, samples), _bits, _float);
        target.Write(_floats.AsSpan(0, samples));
    }

    internal static void ConvertBytes(ReadOnlySpan<byte> src, Span<float> dst, int bits, bool isFloat)
    {
        if (isFloat && bits == 32)
        {
            MemoryMarshal.Cast<byte, float>(src)[..dst.Length].CopyTo(dst);
            return;
        }
        switch (bits)
        {
            case 16:
                var s16 = MemoryMarshal.Cast<byte, short>(src);
                for (int i = 0; i < dst.Length; i++) dst[i] = s16[i] / 32768f;
                break;
            case 24:
                for (int i = 0, b = 0; i < dst.Length; i++, b += 3)
                    dst[i] = ((src[b] << 8) | (src[b + 1] << 16) | (src[b + 2] << 24)) / 2147483648f;
                break;
            case 32:
                var s32 = MemoryMarshal.Cast<byte, int>(src);
                for (int i = 0; i < dst.Length; i++) dst[i] = s32[i] / 2147483648f;
                break;
        }
    }
}
