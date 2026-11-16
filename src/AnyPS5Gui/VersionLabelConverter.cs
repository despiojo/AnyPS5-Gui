using AnyPS5Gui.Core.Components;
using Microsoft.UI.Xaml.Data;

namespace AnyPS5Gui;

/// <summary>
/// Shows a downloaded version by its label, as the list of releases does, instead of by the name of
/// its folder.
/// </summary>
public sealed partial class VersionLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string version ? ComponentStore.VersionLabel(version) : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
