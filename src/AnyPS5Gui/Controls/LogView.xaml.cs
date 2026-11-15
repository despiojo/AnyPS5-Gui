using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace AnyPS5Gui.Controls;

/// <summary>Shows a <see cref="LogBuffer"/> as a scrolling list of lines, with a button to copy all of it.</summary>
public sealed partial class LogView : UserControl
{
    public static readonly DependencyProperty LogProperty = DependencyProperty.Register(
        nameof(Log), typeof(LogBuffer), typeof(LogView),
        new PropertyMetadata(null, (sender, e) => ((LogView)sender).List.ItemsSource = (e.NewValue as LogBuffer)?.Lines));

    public LogView() => InitializeComponent();

    public LogBuffer? Log
    {
        get => (LogBuffer?)GetValue(LogProperty);
        set => SetValue(LogProperty, value);
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Log is null) return;
        var data = new DataPackage();
        data.SetText(Log.GetText());
        Clipboard.SetContent(data);
    }
}
