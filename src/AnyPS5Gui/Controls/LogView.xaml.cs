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
        new PropertyMetadata(null, (sender, _) => ((LogView)sender).Follow()));

    private LogBuffer? _followed;

    public LogView()
    {
        InitializeComponent();
        Loaded += (_, _) => Follow();
        Unloaded += (_, _) => Follow();
    }

    public LogBuffer? Log
    {
        get => (LogBuffer?)GetValue(LogProperty);
        set => SetValue(LogProperty, value);
    }

    /// <summary>
    /// Listens to the buffer in <see cref="Log"/> while the control is on a page, and lets go of it when
    /// it is not. The buffers last as long as the app and every navigation makes a new page: a control
    /// that stayed subscribed would be kept by its buffer, page and all, and go on handling every line.
    /// </summary>
    private void Follow()
    {
        // Loaded and Unloaded can arrive out of order; IsLoaded says where the control is now.
        var log = IsLoaded ? Log : null;
        if (ReferenceEquals(log, _followed)) return;

        if (_followed is not null) _followed.FilePathChanged -= OnFilePathChanged;
        _followed = log;
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
