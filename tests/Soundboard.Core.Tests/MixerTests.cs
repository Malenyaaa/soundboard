using NAudio.Wave;
using Soundboard.Core.Audio;
using Soundboard.Core.Audio.Sinks;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class MixerTests
{
    private static readonly TargetFormat Out = new(48000, 2);

    /// <summary>Endless constant signal in any format (stands in for a microphone or player).</summary>
    private sealed class ConstantProvider(TargetFormat f, float value) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(f.SampleRate, f.Channels);

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Fill(buffer, value, offset, count);
            return count;
        }
    }

    private static float[] Pull(ISampleProvider p, int frames)
    {
        var buf = new float[frames * p.WaveFormat.Channels];
        for (int i = 0; i < buf.Length; i += 960) p.Read(buf, i, Math.Min(960, buf.Length - i));
        return buf;
    }

    [TestMethod]
    public void Ring_buffer_wraps_and_counts_drops()
    {
        var r = new FloatRingBuffer(8);
        Assert.AreEqual(6, r.Write([1, 2, 3, 4, 5, 6]));
        var dst = new float[4];
        Assert.AreEqual(4, r.Read(dst));
        CollectionAssert.AreEqual(new float[] { 1, 2, 3, 4 }, dst);

        Assert.AreEqual(6, r.Write([7, 8, 9, 10, 11, 12])); // wraps around
        Assert.AreEqual(8, r.Available);
        Assert.AreEqual(0, r.Write([13]));
        Assert.AreEqual(1, r.Dropped);

        var all = new float[8];
        Assert.AreEqual(8, r.Read(all));
        CollectionAssert.AreEqual(new float[] { 5, 6, 7, 8, 9, 10, 11, 12 }, all);
    }

    [TestMethod]
    public void Mic_stream_waits_for_preroll_then_counts_underruns()
    {
        var fmt = new TargetFormat(48000, 1);
        var ring = new FloatRingBuffer(48000);
        var p = new RingBufferSampleProvider(ring, fmt, 20); // 960 samples
        var buf = new float[480];

        ring.Write(Enumerable.Repeat(0.5f, 500).ToArray());
        p.Read(buf, 0, buf.Length);
        Assert.IsTrue(buf.All(s => s == 0f), "silent until the pre-roll is there");
        Assert.AreEqual(500, ring.Available, "nothing consumed during pre-roll");

        ring.Write(Enumerable.Repeat(0.5f, 500).ToArray());
        p.Read(buf, 0, buf.Length);
        Assert.IsTrue(buf.All(s => s == 0.5f));

        p.Read(buf, 0, buf.Length); // 520 left -> full
        p.Read(buf, 0, buf.Length); // 40 left -> underrun
        Assert.AreEqual(1, p.Underruns);
        Assert.AreEqual(0f, buf[^1]);
    }

    [TestMethod]
    public void Mic_stream_skips_ahead_when_far_ahead()
    {
        var fmt = new TargetFormat(48000, 1);
        var ring = new FloatRingBuffer(48000);
        var p = new RingBufferSampleProvider(ring, fmt, 20);
        ring.Write(new float[20000]);
        var buf = new float[480];
        p.Read(buf, 0, buf.Length);
        Assert.IsTrue(ring.Available <= 960, $"delay kept near the target, left {ring.Available}");
    }

    [TestMethod]
    public void Mixes_mono_44k_voice_and_stereo_sounds_into_48k_stereo()
    {
        var voice = new ConstantProvider(new TargetFormat(44100, 1), 0.2f);
        var sounds = new ConstantProvider(new TargetFormat(48000, 2), 0.3f);
        var m = new Mixer(Out, voice, sounds);

        var buf = Pull(m, 9600);

        // After the resampler has settled, both channels carry voice + sound.
        Assert.AreEqual(0.5f, buf[2 * 5000], 0.01f);
        Assert.AreEqual(0.5f, buf[2 * 5000 + 1], 0.01f);
    }

    [TestMethod]
    public void Voice_volume_applies()
    {
        var m = new Mixer(Out, new ConstantProvider(Out, 0.4f), null) { VoiceVolume = 0.5f };
        var buf = Pull(m, 4800);
        Assert.AreEqual(0.2f, buf[^1], 1e-4f);
    }

    [TestMethod]
    public void Limiter_keeps_loud_mix_below_one_without_hard_clipping()
    {
        var m = new Mixer(Out, new ConstantProvider(Out, 0.9f), new ConstantProvider(Out, 0.9f));
        var buf = Pull(m, 4800);
        Assert.IsTrue(buf.All(s => s <= 1f && s >= -1f));
        Assert.IsTrue(buf[^1] < 0.9f && buf[^1] > 0.7f, $"limited to the threshold, got {buf[^1]}");
    }

    [TestMethod]
    public void Block_voice_mutes_mic_while_sound_plays()
    {
        bool playing = true;
        var m = new Mixer(Out, new ConstantProvider(Out, 0.3f), null, () => playing) { BlockVoiceWhileSound = true };

        var buf = Pull(m, 4800);
        Assert.AreEqual(0f, buf[^1], 1e-6f, "voice ramped to silence");

        playing = false;
        buf = Pull(m, 4800);
        Assert.AreEqual(0.3f, buf[^1], 1e-4f, "voice back after the sound");
    }

    [TestMethod]
    public void Block_voice_off_by_default()
    {
        var m = new Mixer(Out, new ConstantProvider(Out, 0.3f), null, () => true);
        var buf = Pull(m, 4800);
        Assert.AreEqual(0.3f, buf[^1], 1e-4f);
    }

    [TestMethod]
    public void Peak_meter_reports_and_resets()
    {
        var m = new Mixer(Out, new ConstantProvider(Out, 0.25f), null);
        Pull(m, 480);
        Assert.AreEqual(0.25f, m.TakePeak(), 1e-4f);
        Assert.AreEqual(0f, m.TakePeak());
    }

    [TestMethod]
    public void Test_tone_is_audible_for_one_second()
    {
        var m = new Mixer(Out, null, null);
        m.PlayTestTone();
        var buf = Pull(m, 48000 + 4800);
        Assert.IsTrue(buf.Take(48000).Max() > 0.2f);
        Assert.IsTrue(buf.Skip(2 * 48000).All(s => s == 0f), "silent after one second");
    }

    [TestMethod]
    public void File_sink_writes_voice_and_sound_to_wav()
    {
        using var dir = new TempDir();
        var player = new SoundPlayer(Out);
        var clip = new SoundClip(Enumerable.Repeat(0.25f, 4800 * 2).ToArray(), Out);
        var voice = new ConstantProvider(new TargetFormat(48000, 1), 0.1f); // mono "voice"
        var m = new Mixer(Out, voice, player, () => player.ActiveVoiceCount > 0);
        player.Play(clip, 1f);

        var path = dir.File("mix.wav");
        FileSink.RenderOffline(path, m, 9600);

        using var reader = new WaveFileReader(path);
        Assert.AreEqual(48000, reader.WaveFormat.SampleRate);
        Assert.AreEqual(2, reader.WaveFormat.Channels);
        var samples = new float[9600 * 2];
        reader.ToSampleProvider().Read(samples, 0, samples.Length);
        Assert.AreEqual(0.35f, samples[2 * 2000], 1e-3f, "voice + sound while the clip plays");
        Assert.AreEqual(0.1f, samples[2 * 6000], 1e-3f, "voice alone after the clip");
    }

    [TestMethod]
    public void Capture_converter_handles_pcm_formats()
    {
        var dst = new float[2];
        SampleConverter.ConvertBytes(BitConverter.GetBytes((short)16384).Concat(BitConverter.GetBytes((short)-32768)).ToArray(), dst, 16, false);
        CollectionAssert.AreEqual(new[] { 0.5f, -1f }, dst);

        SampleConverter.ConvertBytes(new byte[] { 0x00, 0x00, 0x40, 0x00, 0x00, 0xC0 }, dst, 24, false);
        CollectionAssert.AreEqual(new[] { 0.5f, -0.5f }, dst);

        SampleConverter.ConvertBytes(BitConverter.GetBytes(0.25f).Concat(BitConverter.GetBytes(-0.75f)).ToArray(), dst, 32, true);
        CollectionAssert.AreEqual(new[] { 0.25f, -0.75f }, dst);
    }

    [TestMethod]
    public void Mixer_read_does_not_allocate()
    {
        // Full chain as in the app: mono 44.1 kHz microphone through the ring buffer,
        // resampled and mapped to 48 kHz stereo, plus the sound player and block-voice check.
        var micFmt = new TargetFormat(44100, 1);
        var ring = new FloatRingBuffer(44100 * 2);
        var voice = new RingBufferSampleProvider(ring, micFmt, 20);
        var player = new SoundPlayer(Out) { Overlap = OverlapMode.Overlay };
        var clips = Enumerable.Range(0, 4).Select(_ => new SoundClip(new float[48000 * 2], Out)).ToArray();
        var m = new Mixer(Out, voice, player, () => player.ActiveVoiceCount > 0) { BlockVoiceWhileSound = true };
        var micChunk = new float[441];
        var buf = new float[480 * 2];

        // Warm up: JIT, resampler buffers, first queue segment.
        for (int i = 0; i < 20; i++)
        {
            ring.Write(micChunk);
            m.Read(buf, 0, buf.Length);
        }
        foreach (var c in clips) player.Play(c, 0.8f);
        m.PlayTestTone();

        long bytes = Alloc.Measure(() =>
        {
            for (int i = 0; i < 300; i++)
            {
                ring.Write(micChunk);
                m.Read(buf, 0, buf.Length);
                m.TakePeak();
            }
        });

        Assert.AreEqual(0, bytes, "audio thread must not allocate");
    }
}
