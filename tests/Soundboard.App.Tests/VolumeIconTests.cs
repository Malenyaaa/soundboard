using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;
using Soundboard.App.Views;

namespace Soundboard.App.Tests;

[TestClass]
public class VolumeIconTests
{
    private static void OnSta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        error?.Throw();
    }

    /// <summary>Opacity of body, three arcs and slash, in drawing order.</summary>
    private static double[] Shown(VolumeIcon icon) =>
        icon.Children.OfType<Path>().Select(p => p.Opacity).ToArray();

    [TestMethod]
    public void Arcs_and_slash_follow_the_step() => OnSta(() =>
    {
        // Not loaded: steps switch at once, without animation.
        var icon = new VolumeIcon { Text = "Volume", MutedText = "Volume: muted" };
        var cases = new (double Percent, double[] Expected)[]
        {
            (0, [1, 0, 0, 0, 1]),
            (20, [1, 1, 0, 0, 0]),
            (50, [1, 1, 1, 0, 0]),
            (90, [1, 1, 1, 1, 0]),
            (0, [1, 0, 0, 0, 1]),
        };
        foreach (var (percent, expected) in cases)
        {
            icon.Percent = percent;
            CollectionAssert.AreEqual(expected, Shown(icon), $"at {percent} %");
        }
    });

    [TestMethod]
    public void Name_says_muted_at_zero() => OnSta(() =>
    {
        var icon = new VolumeIcon { Text = "Volume", MutedText = "Volume: muted", Percent = 0 };
        Assert.AreEqual("Volume: muted", AutomationProperties.GetName(icon));
        Assert.AreEqual("Volume: muted", icon.ToolTip);
        icon.Percent = 1;
        Assert.AreEqual("Volume", AutomationProperties.GetName(icon));
        Assert.AreEqual("Volume", icon.ToolTip);
    });
}
