using AnyPS5Gui.Core;
using AnyPS5Gui.Core.Components;
using AnyPS5Gui.Core.Library;
using AnyPS5Gui.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AnyPS5Gui;

public partial class App : Application
{
    /// <summary>
    /// The app's own data lives in %LOCALAPPDATA%\AnyPS5Gui. ANYPS5GUI_DATA names another folder,
    /// for a portable copy or for trying things without touching the real settings.
    /// </summary>
    public static AppPaths Paths { get; } =
        Environment.GetEnvironmentVariable("ANYPS5GUI_DATA") is { Length: > 0 } custom
            ? new AppPaths(Path.GetFullPath(custom))
            : AppPaths.Default();

    public static AppSettings Settings { get; private set; } = new();
    public static GameLibrary Library { get; private set; } = null!;
    public static ComponentStore Components { get; private set; } = null!;
    public static HttpClient Http { get; } = ReleaseClient.CreateHttpClient();
    public static HostCheck Host { get; } = HostCheck.Run();

    public static MainWindow Window { get; private set; } = null!;

    // One of each for the whole run: a conversion or a download keeps going while another page is shown.
    public static ComponentsViewModel ComponentsViewModel { get; private set; } = null!;
    public static ConvertViewModel ConvertViewModel { get; private set; } = null!;
    public static GamesViewModel GamesViewModel { get; private set; } = null!;
    public static SettingsViewModel SettingsViewModel { get; private set; } = null!;

    public App()
    {
        Settings = JsonFile.Load<AppSettings>(Paths.SettingsFile);
        Library = new GameLibrary(Paths.LibraryFile);
        Library.Load();
        Components = new ComponentStore(Paths.ComponentsDirectory);

        // Has to happen before the first resource is read.
        if (!string.IsNullOrEmpty(Settings.Language))
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = Settings.Language;

        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    public static string ErrorLog => Path.Combine(Paths.Root, "errors.log");

    public static void SaveSettings() => JsonFile.Save(Paths.SettingsFile, Settings);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ComponentsViewModel = new ComponentsViewModel();
        ConvertViewModel = new ConvertViewModel();
        GamesViewModel = new GamesViewModel();
        SettingsViewModel = new SettingsViewModel();

        Window = new MainWindow();
        Window.ApplyTheme(Settings.Theme);
        Window.Activate();
    }

    // An error nothing expected. Closing the app would also kill a running game or conversion, so the
    // window stays; the details go to a file that can be attached to a bug report.
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            Directory.CreateDirectory(Paths.Root);
            File.AppendAllText(ErrorLog, $"{DateTimeOffset.Now:u} {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Nowhere to write it; the dialog below still shows the message.
        }

        if (Window?.Content?.XamlRoot is not { } root) return;
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Loc.Get("Error_Title"),
            Content = new TextBlock
            {
                Text = e.Message + Environment.NewLine + Environment.NewLine + Loc.Format("Error_Details", ErrorLog),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            },
            CloseButtonText = Loc.Get("Error_Close"),
        };
        // Only one dialog can be open at a time; a second error while one shows is already in the file.
        _ = dialog.ShowAsync().AsTask().ContinueWith(task => _ = task.Exception, TaskScheduler.Default);
    }
}
