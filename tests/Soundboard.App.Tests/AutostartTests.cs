using Soundboard.App;
using Soundboard.Core.Text;

namespace Soundboard.App.Tests;

/// <summary>"Start with Windows" against a fake Run key – the real registry is never touched.</summary>
[TestClass]
public sealed class AutostartTests
{
    private const string Exe = @"C:\Apps\Soundboard\Soundboard.exe";
    private const string OtherExe = @"D:\Old copy\Soundboard.exe";

    private sealed class FakeRunKey : IRunKey
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Exception? Refuse { get; set; }

        public string? Read(string name) => Values.TryGetValue(name, out var v) ? v : null;

        public void Write(string name, string value)
        {
            if (Refuse is not null) throw Refuse;
            Values[name] = value;
        }

        public void Delete(string name)
        {
            if (Refuse is not null) throw Refuse;
            Values.Remove(name);
        }
    }

    [TestMethod]
    public void Enable_writes_the_quoted_path_with_tray_under_the_installer_name()
    {
        var key = new FakeRunKey();
        var a = new Autostart(key, Exe);
        Assert.AreEqual(AutostartKind.Off, a.Read().Kind);

        Assert.IsNull(a.Enable());

        Assert.AreEqual("Soundboard", Autostart.ValueName);
        Assert.AreEqual("\"" + Exe + "\" --tray", key.Values["Soundboard"]);
        Assert.AreEqual(AutostartKind.ThisCopy, a.Read().Kind);
    }

    [TestMethod]
    public void Disable_removes_the_entry()
    {
        var key = new FakeRunKey();
        key.Values["Soundboard"] = Autostart.CommandFor(Exe);
        var a = new Autostart(key, Exe);

        Assert.IsNull(a.Disable());

        Assert.IsFalse(key.Values.ContainsKey("Soundboard"));
        Assert.AreEqual(AutostartKind.Off, a.Read().Kind);
        Assert.IsNull(a.Disable(), "removing a missing entry is no error");
    }

    [TestMethod]
    public void An_entry_set_by_the_installer_counts_as_this_copy()
    {
        var key = new FakeRunKey();
        key.Values["Soundboard"] = "\"c:\\apps\\soundboard\\SOUNDBOARD.EXE\" --tray";
        Assert.AreEqual(AutostartKind.ThisCopy, new Autostart(key, Exe).Read().Kind);
    }

    [TestMethod]
    public void An_entry_for_another_copy_names_it_and_can_be_switched_over()
    {
        var key = new FakeRunKey();
        key.Values["Soundboard"] = Autostart.CommandFor(OtherExe);
        var a = new Autostart(key, Exe);

        var state = a.Read();
        Assert.AreEqual(AutostartKind.OtherCopy, state.Kind);
        Assert.AreEqual(OtherExe, state.OtherPath);

        Assert.IsNull(a.Enable());
        Assert.AreEqual(AutostartKind.ThisCopy, a.Read().Kind);
    }

    [TestMethod]
    public void Unquoted_paths_are_read_up_to_the_exe()
    {
        Assert.AreEqual(@"C:\Program Files\Soundboard\Soundboard.exe", Autostart.ExeOf(@"C:\Program Files\Soundboard\Soundboard.exe --tray"));
        Assert.AreEqual(OtherExe, Autostart.ExeOf("\"" + OtherExe + "\""));
        Assert.IsNull(Autostart.ExeOf("  "));
    }

    [TestMethod]
    public void A_refused_write_gives_the_reason_and_changes_nothing()
    {
        var key = new FakeRunKey { Refuse = new UnauthorizedAccessException("blocked by policy") };
        var a = new Autostart(key, Exe);

        Assert.AreEqual("blocked by policy", a.Enable());
        Assert.AreEqual(AutostartKind.Off, a.Read().Kind);

        key.Values["Soundboard"] = Autostart.CommandFor(Exe);
        Assert.AreEqual("blocked by policy", a.Disable());
        Assert.AreEqual(AutostartKind.ThisCopy, a.Read().Kind);
    }

    [TestMethod]
    public void Without_a_program_path_nothing_is_written()
    {
        var key = new FakeRunKey();
        Assert.AreEqual(L.Get("Autostart.NoPath"), new Autostart(key, null).Enable());
        Assert.AreEqual(0, key.Values.Count);
    }

    [TestMethod]
    public void Removal_guide_removes_only_an_entry_for_this_copy()
    {
        var key = new FakeRunKey();
        key.Values["Soundboard"] = Autostart.CommandFor(OtherExe);
        var a = new Autostart(key, Exe);
        Assert.IsFalse(a.RemoveIfThisCopy(out var err));
        Assert.IsNull(err);
        Assert.IsTrue(key.Values.ContainsKey("Soundboard"), "another copy's entry stays");

        key.Values["Soundboard"] = Autostart.CommandFor(Exe);
        Assert.IsTrue(a.RemoveIfThisCopy(out err));
        Assert.IsNull(err);
        Assert.IsFalse(key.Values.ContainsKey("Soundboard"));
    }
}
