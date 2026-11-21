using AnyPS5Gui.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace AnyPS5Gui.Pages;

public sealed partial class ConvertPage : Page
{
    public ConvertPage() => InitializeComponent();

    public ConvertViewModel ViewModel => App.ConvertViewModel;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems)) e.AcceptedOperation = DataPackageOperation.Link;
    }

    // A game folder, or its executable, dropped anywhere on the page.
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count > 0 && !string.IsNullOrEmpty(items[0].Path)) ViewModel.OpenGame(items[0].Path);
    }

    private void OnVersionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ConvertVersionBox.SelectedItem is string version) ViewModel.SelectedVersion = version;
    }

    private void OnUseForkClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ForkChoiceItem fork }) ViewModel.UseForkCommand.Execute(fork);
    }
}
