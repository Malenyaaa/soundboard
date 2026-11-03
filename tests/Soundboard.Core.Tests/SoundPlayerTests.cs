using Soundboard.Core.Audio;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class SoundPlayerTests
{
    private static readonly TargetFormat Stereo = new(48000, 2);

    private static SoundClip Constant(float value, int frames) =>
        new(Enumerable.Repeat(value, frames * Stereo.Channels).ToArray(), Stereo);

    private static float[] Pull(SoundPlayer p, int frames)
    {
        var buf = new float[frames * Stereo.Channels];
        p.Read(buf, 0, buf.Length);
        return buf;
    }

    [TestMethod]
    public void Plays_clip_with_tile_and_master_volume()
    {
        var p = new SoundPlayer(Stereo) { MasterVolume = 0.5f };
        var clip = Constant(0.4f, 10000);

        p.Play(clip, 0.5f);
        var buf = Pull(p, 1000);

        Assert.AreEqual(0.1f, buf[0], 1e-6f, "starts at full level, no fade-in");
        Assert.AreEqual(0.1f, buf[^1], 1e-6f);
        Assert.IsNotNull(p.GetProgress(clip.Handle));
    }

    [TestMethod]
    public void Clip_ends_and_is_no_longer_playing()
    {
        var p = new SoundPlayer(Stereo);
        var clip = Constant(0.3f, 100);

        p.Play(clip, 1f);
        var buf = Pull(p, 300);

        Assert.AreEqual(0.3f, buf[2 * 99], 1e-6f);
        Assert.AreEqual(0f, buf[2 * 100]);
        Assert.IsNull(p.GetProgress(clip.Handle));
        Assert.AreEqual(0, p.ActiveVoiceCount);
    }

    [TestMethod]
    public void New_sound_stops_old_by_default()
    {
        var p = new SoundPlayer(Stereo);
        var a = Constant(0.2f, 48000);
        var b = Constant(0.3f, 48000);

        p.Play(a, 1f);
        Pull(p, 480);
        p.Play(b, 1f);
        var buf = Pull(p, 1000);

        Assert.AreEqual(0.3f, buf[2 * 900], 1e-6f, "after the short fade only the new sound remains");
        Assert.IsNull(p.GetProgress(a.Handle));
        Assert.IsNotNull(p.GetProgress(b.Handle));
    }

    [TestMethod]
    public void Overlay_mode_mixes_both()
    {
        var p = new SoundPlayer(Stereo) { Overlap = OverlapMode.Overlay };
        var a = Constant(0.2f, 48000);
        var b = Constant(0.3f, 48000);

        p.Play(a, 1f);
        p.Play(b, 1f);
        var buf = Pull(p, 1000);

        Assert.AreEqual(0.5f, buf[2 * 500], 1e-6f);
    }

    [TestMethod]
    public void Stop_all_fades_out_within_one_ramp()
    {
        var p = new SoundPlayer(Stereo);
        var clip = Constant(0.5f, 48000);
        p.Play(clip, 1f);
        Pull(p, 480);

        p.StopAll();
        var buf = Pull(p, 1000);

        Assert.IsTrue(buf[0] > 0f && buf[0] < 0.5f, "fade starts below full level");
        Assert.AreEqual(0f, buf[2 * SoundPlayer.RampFrames]);
        Assert.AreEqual(0, p.ActiveVoiceCount);
    }

    [TestMethod]
    public void Volume_change_reaches_target_after_ramp()
    {
        var p = new SoundPlayer(Stereo);
        var clip = Constant(0.5f, 48000);
        p.Play(clip, 1f);
        Pull(p, 480);

        p.SetVolume(clip.Handle, 0.2f);
        var buf = Pull(p, 1000);

        Assert.AreEqual(0.1f, buf[2 * 600], 1e-5f);
    }

    [TestMethod]
    public void Soft_clip_keeps_output_below_one()
    {
        var p = new SoundPlayer(Stereo) { Overlap = OverlapMode.Overlay };
        for (int i = 0; i < 5; i++) p.Play(Constant(0.9f, 4800), 1f);

        var buf = Pull(p, 1000);

        Assert.IsTrue(buf.All(s => s <= 1f && s >= -1f));
        Assert.IsTrue(buf[100] > 0.95f, "loud but limited");
    }

    [TestMethod]
    public void Soft_clip_is_linear_below_knee()
    {
        var buf = new[] { 0.5f, -0.79f, 0f };
        SoundPlayer.SoftClip(buf, 0, buf.Length);
        CollectionAssert.AreEqual(new[] { 0.5f, -0.79f, 0f }, buf);
    }

    [TestMethod]
    public void Rejects_clip_in_other_format()
    {
        var p = new SoundPlayer(Stereo);
        var clip = new SoundClip(new float[10], new TargetFormat(44100, 2));
        Assert.ThrowsException<ArgumentException>(() => p.Play(clip, 1f));
    }

    [TestMethod]
    public void Read_does_not_allocate()
    {
        var p = new SoundPlayer(Stereo) { Overlap = OverlapMode.Overlay };
        var clips = Enumerable.Range(0, 8).Select(_ => Constant(0.1f, 48000)).ToArray();
        var buf = new float[480 * 2];
        // Warm up (JIT, first queue segment).
        p.Play(clips[0], 1f);
        p.Read(buf, 0, buf.Length);

        foreach (var c in clips) p.Play(c, 0.7f);
        p.SetVolume(clips[1].Handle, 0.3f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) p.Read(buf, 0, buf.Length);
        p.StopAll();
        p.Read(buf, 0, buf.Length);
        long after = GC.GetAllocatedBytesForCurrentThread();

        // StopAll() itself runs on this thread too; it enqueues into an existing segment.
        Assert.AreEqual(0, after - before, "audio thread must not allocate");
    }

    [TestMethod]
    public void Reset_clears_voices_and_queue()
    {
        var p = new SoundPlayer(Stereo);
        var clip = Constant(0.5f, 48000);
        p.Play(clip, 1f);
        Pull(p, 10);
        p.Play(clip, 1f);

        p.Reset();
        var buf = Pull(p, 10);

        Assert.IsTrue(buf.All(s => s == 0f));
    }
}
