using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace AnyPS5Gui.Controls;

/// <summary>
/// Shows a <see cref="LogBuffer"/> as a scrolling list of lines, with buttons to copy what is listed
/// and to open the text file that holds the whole run.
/// </summary>
public sealed partial class LogView : UserControl
{
    public static readonly DependencyProperty LogProperty = DependencyProperty.Register(
        nameof(Log), typeof(LogBuffer), typeof(LogView),
        new PropertyMetadata(null, (sender, e) => ((LogView)sender).OnLogChanged(e.OldValue as LogBuffer, e.NewValue as LogBuffer)));

    public LogView() => InitializeComponent();

    public LogBuffer? Log
    {
        get => (LogBuffer?)GetValue(LogProperty);
        set => SetValue(LogProperty, value);
    }

    private void OnLogChanged(LogBuffer? old, LogBuffer? log)
    {
        if (old is not null) old.FilePathChanged -= OnFilePathChanged;
        if (log is not null) log.FilePathChanged += OnFilePathChanged;
        List.ItemsSource = log?.Lines;
        OpenFileButton.IsEnabled = log?.FilePath is not null;
    }

    private void OnFilePathChanged(object? sender, EventArgs e) => OpenFileButton.IsEnabled = Log?.FilePath is not null;

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Log is null) return;
        var data = new DataPackage();
        data.SetText(Log.GetText());
        Clipboard.SetContent(data);
    }

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        // Gone when enough later runs have pushed it out of the ones that are kept.
        if (Log?.FilePath is not { } path || !File.Exists(path))
        {
            OpenFileButton.IsEnabled = false;
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception)
        {
            // Nothing is registered to open .log files: show the file in its folder instead.
            var explorer = new ProcessStartInfo("explorer.exe");
            explorer.ArgumentList.Add("/select," + path);
            Process.Start(explorer)?.Dispose();
        }
    }
}
