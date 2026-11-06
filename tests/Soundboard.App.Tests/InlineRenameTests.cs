using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Soundboard.App.Views;

namespace Soundboard.App.Tests;

[TestClass]
public class InlineRenameTests
{
    private sealed class Item : IInlineRenamable
    {
        public string Name { get; set; } = "Airhorn";
        public bool IsRenaming { get; set; } = true;
    }

    private static (TextBox Box, Item Item) Open(string typed)
    {
        var item = new Item();
        var box = new TextBox { DataContext = item, Text = typed };
        InlineRename.SetIsEnabled(box, true);
        return (box, item);
    }

    [TestMethod]
    public void Leaving_the_box_keeps_the_typed_name() => PanelMotionTests.OnSta(() =>
    {
        var (box, item) = Open("  Horn  ");
        box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, box, null) { RoutedEvent = UIElement.LostKeyboardFocusEvent });
        Assert.AreEqual("Horn", item.Name, "trimmed");
        Assert.IsFalse(item.IsRenaming);
    });

    [TestMethod]
    public void Enter_keeps_and_Esc_discards() => PanelMotionTests.OnSta(() =>
    {
        var (box, item) = Open("Horn");
        Assert.IsTrue(InlineRename.HandleKey(box, Key.Enter));
        Assert.AreEqual("Horn", item.Name);

        (box, item) = Open("Horn");
        Assert.IsTrue(InlineRename.HandleKey(box, Key.Escape));
        Assert.AreEqual("Airhorn", item.Name, "Esc keeps the old name");
        Assert.IsFalse(item.IsRenaming);
        // The box losing the focus after Esc changes nothing any more.
        box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, box, null) { RoutedEvent = UIElement.LostKeyboardFocusEvent });
        Assert.AreEqual("Airhorn", item.Name);

        Assert.IsFalse(InlineRename.HandleKey(box, Key.A), "other keys go to the text");
    });

    [TestMethod]
    public void An_empty_name_keeps_the_old_one() => PanelMotionTests.OnSta(() =>
    {
        var (box, item) = Open("   ");
        InlineRename.Confirm(box);
        Assert.AreEqual("Airhorn", item.Name);
        Assert.IsFalse(item.IsRenaming);
    });

    [TestMethod]
    public void A_click_outside_keeps_the_name_a_click_inside_does_not() => PanelMotionTests.OnSta(() =>
    {
        var (box, item) = Open("Horn");
        var root = new Grid();
        var elsewhere = new Border();
        root.Children.Add(box);
        root.Children.Add(elsewhere);

        Assert.IsFalse(InlineRename.ConfirmIfOutside(box, box), "a click into the box goes on editing");
        Assert.IsTrue(item.IsRenaming);
        Assert.IsTrue(InlineRename.ConfirmIfOutside(box, elsewhere));
        Assert.AreEqual("Horn", item.Name);
        Assert.IsFalse(item.IsRenaming);
        Assert.IsFalse(InlineRename.ConfirmIfOutside(box, elsewhere), "nothing open any more");
        Assert.IsFalse(InlineRename.ConfirmIfOutside(null, elsewhere));
    });
}
