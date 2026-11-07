using System.Windows;
using System.Windows.Controls;
using Soundboard.App.ViewModels;

namespace Soundboard.App.Views;

/// <summary>The pad grid shows pads and group heads: each gets its own template.</summary>
public sealed class PadItemTemplates : DataTemplateSelector
{
    public DataTemplate? Pad { get; set; }

    public DataTemplate? Head { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is CategoryViewModel ? Head : Pad;
}
