using AnyPS5Gui.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AnyPS5Gui.Pages;

public sealed partial class GamesPage : Page
{
    public GamesPage()
    {
        InitializeComponent();
        // Before the bindings run: a selected index applied to a box without items would be lost.
        LanguageBox.ItemsSource = ViewModel.Languages;
    }

    public GamesViewModel ViewModel => App.GamesViewModel;

    // x:Bind cannot write an object-typed SelectedItem back into a typed property. A list that loses
    // its selection while an item is being removed is not the user choosing "nothing".
    private void OnGameSelected(object sender, SelectionChangedEventArgs e)
    {
        if (GameList.SelectedItem is GameItemViewModel game) ViewModel.Selected = game;
    }

    private void OnLibraryVersionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.Selected is { } game && LibraryVersionBox.SelectedItem is string version)
            game.SelectedLibraryVersion = version;
    }
}
