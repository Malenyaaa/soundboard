using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Soundboard.Core.Text;

namespace Soundboard.App.Text;

/// <summary>
/// The texts for XAML: <c>{t:T Settings.Title}</c> binds to the text with that key and follows a change
/// of the interface language at once.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Texts.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>Source of the XAML texts: an indexer over the text keys that announces a language change.</summary>
public sealed class Texts : INotifyPropertyChanged
{
    public static Texts Instance { get; } = new();

    private Texts() => L.Changed += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    public string this[string key] => L.Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Numbers and dates of WPF follow the interface language (culture of the thread and xml:lang of the windows).</summary>
public static class UiLanguage
{
    private static readonly List<WeakReference<FrameworkElement>> Elements = [];
    private static bool _hooked;

    /// <summary>Sets the culture for the current language; called at start and after every change.</summary>
    public static void ApplyCulture()
    {
        var culture = L.Culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        if (_hooked) return;
        _hooked = true;
        L.Changed += (_, _) =>
        {
            ApplyCulture();
            var lang = XmlLanguage.GetLanguage(L.Culture.IetfLanguageTag);
            Elements.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var w in Elements)
                if (w.TryGetTarget(out var e)) e.Language = lang;
        };
    }

    /// <summary>Gives a window (or another root) the language of the interface and keeps it up to date.</summary>
    public static void Attach(FrameworkElement element)
    {
        ApplyCulture();
        element.Language = XmlLanguage.GetLanguage(L.Culture.IetfLanguageTag);
        Elements.Add(new WeakReference<FrameworkElement>(element));
    }
}
