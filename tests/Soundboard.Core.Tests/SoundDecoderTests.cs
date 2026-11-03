using Soundboard.Core.Audio;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class SoundDecoderTests
{
    [TestMethod]
    public void Decodes_wav_in_same_format_exactly()
    {
        using var dir = new TempDir();
        var path = dir.File("const.wav");
        WavFactory.WriteConstant(path, 48000, 2, 4800, 0.25f);

        var clip = SoundDecoder.Decode(path, new TargetFormat(48000, 2));

        Assert.AreEqual(4800, clip.FrameCount);
        Assert.AreEqual(new TargetFormat(48000, 2), clip.Format);
        Assert.IsTrue(clip.Samples.All(s => Math.Abs(s - 0.25f) < 1e-6f));
    }

    [TestMethod]
    public void Resamples_and_upmixes_mono_44k_to_stereo_48k()
    {
        using var dir = new TempDir();
        var path = dir.File("sine.wav");
        WavFactory.WriteSine(path, 44100, 1, 1.0, amplitude: 0.5);

        var clip = SoundDecoder.Decode(path, new TargetFormat(48000, 2));

        Assert.AreEqual(2, clip.Format.Channels);
        Assert.AreEqual(48000, clip.FrameCount, 200, "about one second at 48 kHz");
        for (int f = 0; f < clip.FrameCount; f++)
            Assert.AreEqual(clip.Samples[2 * f], clip.Samples[2 * f + 1]);
        float peak = clip.Samples.Max(Math.Abs);
        Assert.AreEqual(0.5f, peak, 0.03f);
    }

    [TestMethod]
    public void Downmixes_stereo_to_mono_by_average()
    {
        var src = new float[] { 1f, 0f, 0.5f, -0.5f };
        var dst = SoundDecoder.MapChannels(src, 2, 2, 1);
        CollectionAssert.AreEqual(new[] { 0.5f, 0f }, dst);
    }

    [TestMethod]
    public void Stereo_to_six_channels_fills_front_and_leaves_rest_silent()
    {
        var src = new float[] { 0.1f, 0.2f };
        var dst = SoundDecoder.MapChannels(src, 1, 2, 6);
        CollectionAssert.AreEqual(new[] { 0.1f, 0.2f, 0f, 0f, 0f, 0f }, dst);
    }

    [TestMethod]
    public void Unsupported_extension_gives_german_hint()
    {
        using var dir = new TempDir();
        var path = dir.File("notiz.txt");
        File.WriteAllText(path, "kein Ton");

        var ex = Assert.ThrowsException<SoundLoadException>(() => SoundDecoder.Decode(path, TargetFormat.Fallback));
        StringAssert.Contains(ex.Message, "nicht unterstützt");
    }

    [TestMethod]
    public void Missing_file_gives_hint()
    {
        var ex = Assert.ThrowsException<SoundLoadException>(() => SoundDecoder.Decode(@"Z:\gibt-es-nicht\x.wav", TargetFormat.Fallback));
        StringAssert.Contains(ex.Message, "fehlt");
    }

    [TestMethod]
    public void Broken_wav_gives_hint_instead_of_crash()
    {
        using var dir = new TempDir();
        var path = dir.File("kaputt.wav");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        Assert.ThrowsException<SoundLoadException>(() => SoundDecoder.Decode(path, TargetFormat.Fallback));
    }

    [TestMethod]
    public async Task SoundBank_loads_and_resets()
    {
        using var dir = new TempDir();
        var path = dir.File("a.wav");
        WavFactory.WriteConstant(path, 48000, 2, 100, 0.1f);
        var bank = new SoundBank(new TargetFormat(48000, 2));
        var id = Guid.NewGuid();

        await bank.LoadAsync(id, path);
        Assert.IsTrue(bank.TryGet(id, out var clip));
        Assert.AreEqual(100, clip.FrameCount);

        bank.Reset(new TargetFormat(44100, 1));
        Assert.IsFalse(bank.TryGet(id, out _));
        Assert.AreEqual(new TargetFormat(44100, 1), bank.Target);
    }
}
