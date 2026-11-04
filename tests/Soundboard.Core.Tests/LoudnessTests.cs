using Soundboard.Core.Audio;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class LoudnessTests
{
    private static float[] Constant(float value, int frames, int channels = 2) => Enumerable.Repeat(value, frames * channels).ToArray();

    [TestMethod]
    public void Rms_and_peak_of_a_steady_signal()
    {
        var info = Loudness.Analyze(Constant(0.5f, 48000), 2, 48000);
        Assert.AreEqual(0.5, info.Rms, 1e-6);
        Assert.AreEqual(0.5, info.Peak, 1e-6);
    }

    [TestMethod]
    public void Silence_around_a_sound_does_not_make_it_quieter()
    {
        // 1 s silence, 0.5 s at 0.4, 1 s silence: the RMS is that of the audible part.
        var s = Constant(0f, 48000).Concat(Constant(0.4f, 24000)).Concat(Constant(0f, 48000)).ToArray();
        var info = Loudness.Analyze(s, 2, 48000);
        Assert.AreEqual(0.4, info.Rms, 1e-6);
        Assert.IsTrue(Loudness.Analyze(Constant(0f, 4800), 2, 48000).IsSilent);
        Assert.IsTrue(Loudness.Analyze([], 2, 48000).IsSilent);
    }

    [TestMethod]
    public void Loud_pads_come_down_to_the_middle_quiet_ones_stay_at_full()
    {
        var quiet = new LoudnessInfo(0.2, 0.1);
        var middle = new LoudnessInfo(0.6, 0.2);
        var loud = new LoudnessInfo(1.0, 0.5);
        double target = Loudness.Target([quiet, middle, loud, default]);
        Assert.AreEqual(0.2, target, 1e-9, "median of the audible ones; silence does not count");
        Assert.AreEqual(1f, Loudness.VolumeFor(quiet, target), "never louder than the file");
        Assert.AreEqual(1f, Loudness.VolumeFor(middle, target));
        Assert.AreEqual(0.4f, Loudness.VolumeFor(loud, target));
        Assert.IsNull(Loudness.VolumeFor(default, target), "silence is left alone");
    }

    [TestMethod]
    public void Even_count_takes_the_mean_of_the_two_in_the_middle_and_rounds_to_whole_percent()
    {
        double target = Loudness.Target([new(0.5, 0.1), new(0.5, 0.3)]);
        Assert.AreEqual(0.2, target, 1e-9);
        Assert.AreEqual(0.67f, Loudness.VolumeFor(new LoudnessInfo(0.9, 0.3), target));
        Assert.AreEqual(0, Loudness.Target([]));
    }
}
