using System.Runtime.InteropServices;
using AnyPS5Gui.Core;
using AnyPS5Gui.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace AnyPS5Gui;

public sealed partial class MainWindow : Window
{
    public const string ConvertTag = "convert";
    public const string GamesTag = "games";

    public MainWindow()
    {
        InitializeComponent();

        Title = "AnyPS5 GUI";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AnyPS5Gui.ico"));

        // AppWindow sizes are in physical pixels.
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1180 * scale), (int)(860 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(860 * scale);
            presenter.PreferredMinimumHeight = (int)(600 * scale);
        }

        // Output is read through pipes this process owns: a game or relinker left behind would lose them.
        Closed += (_, _) =>
        {
            App.ConvertViewModel.Cancel();
            App.ComponentsViewModel.CancelCommand.Execute(null);
            App.GamesViewModel.StopAll();
        };

        Navigation.SelectedItem = Navigation.MenuItems[0];
    }

    public void Navigate(string tag)
    {
        Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(item => (string)item.Tag == tag) ?? Navigation.SelectedItem;
    }

    public void ApplyTheme(AppTheme theme)
    {
        Root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        // The caption buttons are drawn by the system and need telling separately.
        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : (args.SelectedItemContainer?.Tag as string) == GamesTag ? typeof(GamesPage) : typeof(ConvertPage);
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}
