using Soundboard.Core.Audio;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class SoundTrimTests
{
    private static readonly TargetFormat Stereo = new(1000, 2);

    /// <summary>Two seconds at 1000 Hz stereo; every frame holds its own index (left) and minus it (right).</summary>
    private static SoundClip Ramp()
    {
        var s = new float[2000 * 2];
        for (int f = 0; f < 2000; f++) { s[f * 2] = f; s[f * 2 + 1] = -f; }
        return new SoundClip(s, Stereo);
    }

    [TestMethod]
    public void Nothing_cut_gives_the_clip_itself()
    {
        var full = Ramp();
        Assert.AreSame(full, SoundTrim.Apply(full, null, null));
        Assert.AreSame(full, SoundTrim.Apply(full, 0, 2.0), "start at 0 and end at the end is no cut");
        Assert.AreEqual((null, null), SoundTrim.Normalize(0, 2.0, 2.0));
    }

    [TestMethod]
    public void Cut_keeps_whole_frames_from_start_to_end()
    {
        var part = SoundTrim.Apply(Ramp(), 0.5, 1.25);
        Assert.AreEqual(750, part.FrameCount);
        Assert.AreEqual(500f, part.Samples[0]);
        Assert.AreEqual(-500f, part.Samples[1], "channels stay paired");
        Assert.AreEqual(1249f, part.Samples[^2]);
    }

    [TestMethod]
    public void Out_of_range_and_crossed_edges_are_clamped_to_the_shortest_part()
    {
        Assert.AreEqual((1.5, (double?)null), SoundTrim.Normalize(1.5, 9, 2.0));
        var (s, e) = SoundTrim.Normalize(1.2, 1.0, 2.0); // crossed
        Assert.AreEqual(1.2, s!.Value, 1e-9);
        Assert.AreEqual(1.2 + SoundTrim.MinLength, e!.Value, 1e-9);
        var (s2, e2) = SoundTrim.Normalize(2.0, 2.0, 2.0); // at the very end: the last bit
        Assert.AreEqual(2.0 - SoundTrim.MinLength, s2!.Value, 1e-9);
        Assert.IsNull(e2);
    }

    [TestMethod]
    public void Peaks_take_the_loudest_sample_per_column()
    {
        var s = new float[] { 0.1f, -0.9f, 0.2f, 0.3f, -0.5f, 0.4f, 0f, 0f };
        var peaks = SoundTrim.Peaks(new SoundClip(s, new TargetFormat(1000, 2)), 2);
        CollectionAssert.AreEqual(new[] { 0.9f, 0.5f }, peaks);
    }
}
