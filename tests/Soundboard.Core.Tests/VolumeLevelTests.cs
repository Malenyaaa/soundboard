using Soundboard.Core.Audio;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class VolumeLevelTests
{
    [TestMethod]
    [DataRow(0.0, 0)]
    [DataRow(1.0, 1)]
    [DataRow(33.0, 1)]
    [DataRow(34.0, 2)]
    [DataRow(66.0, 2)]
    [DataRow(67.0, 3)]
    [DataRow(100.0, 3)]
    public void Steps_follow_the_thirds(double percent, int level) =>
        Assert.AreEqual(level, VolumeLevel.Of(percent));

    [TestMethod]
    [DataRow(0.4, 0)]   // the number reads "0 %": struck through
    [DataRow(0.5, 0)]   // rounds to even, still "0 %"
    [DataRow(0.6, 1)]   // reads "1 %"
    [DataRow(33.4, 1)]
    [DataRow(33.6, 2)]
    [DataRow(66.6, 3)]
    public void Rounds_like_the_number_beside_the_slider(double percent, int level) =>
        Assert.AreEqual(level, VolumeLevel.Of(percent));

    [TestMethod]
    [DataRow(-5.0, 0)]
    [DataRow(250.0, 3)]
    [DataRow(double.NaN, 0)]
    public void Out_of_range_stays_in_the_steps(double percent, int level) =>
        Assert.AreEqual(level, VolumeLevel.Of(percent));
}
