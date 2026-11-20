using AnyPS5Gui.Core.Components;
using AnyPS5Gui.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AnyPS5Gui.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage() => InitializeComponent();

    public SettingsViewModel ViewModel => App.SettingsViewModel;

    // x:Bind cannot write an object-typed SelectedItem back into a typed property. A box that loses
    // its selection because its list is being refilled is not the user choosing "nothing".
    private void OnActiveVersionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (InstalledBox.SelectedItem is string version) ViewModel.Components.ActiveVersion = version;
    }

    private void OnReleaseSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ReleaseBox.SelectedItem is ReleaseInfo release) ViewModel.Components.SelectedRelease = release;
    }

    private void OnKnownForkSelected(object sender, SelectionChangedEventArgs e)
    {
        if (KnownForkBox.SelectedItem is KnownForkItem fork) ViewModel.SelectedKnownFork = fork;
    }
}
