using Soundboard.Core.Audio;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class AudioStateTextTests
{
    [TestMethod]
    public void Report_lists_defaults_and_devices()
    {
        var report = new AudioStateReport(
            new DateTime(2026, 1, 2, 3, 4, 5),
            [
                new DefaultEndpoint(DeviceFlow.Render, DeviceRoleKind.Console, "id-a", "Lautsprecher"),
                new DefaultEndpoint(DeviceFlow.Capture, DeviceRoleKind.Communications, null, null),
            ],
            [
                new AudioDeviceInfo("id-a", "Lautsprecher", DeviceFlow.Render, DeviceStatus.Active,
                    [DeviceRoleKind.Console], 0.455f, true, new MixFormatInfo(48000, 2, 32, true)),
                new AudioDeviceInfo("id-b", "Mikrofon", DeviceFlow.Capture, DeviceStatus.Unplugged, [], null, null, null),
            ]);

        var text = AudioStateText.Format(report);

        StringAssert.Contains(text, "Wiedergabe / Konsole");
        StringAssert.Contains(text, "Lautsprecher  [id-a]");
        StringAssert.Contains(text, "(keins)");
        StringAssert.Contains(text, "46\u202F%, stumm");
        StringAssert.Contains(text, "48.000 Hz, 2 Kanäle, 32 bit Float");
        StringAssert.Contains(text, "Mikrofon  (nicht angeschlossen)");
        StringAssert.Contains(text, "ID: id-b");
    }
}
