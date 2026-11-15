using Microsoft.Windows.Storage.Pickers;

namespace AnyPS5Gui;

/// <summary>The system dialogs for choosing a folder or a file. Null when the user cancels.</summary>
public static class Pickers
{
    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker(App.Window.AppWindow.Id);
        var result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }

    public static async Task<string?> PickFileAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker(App.Window.AppWindow.Id);
        foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }
}
