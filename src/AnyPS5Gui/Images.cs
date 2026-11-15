using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AnyPS5Gui;

public static class Images
{
    /// <summary>A game's icon, or null when it has none or it cannot be read.</summary>
    public static ImageSource? Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            // Through a stream, not a file URI: folder names with # or % in them do not survive as URIs.
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage { DecodePixelHeight = 192 };
            bitmap.SetSource(stream.AsRandomAccessStream());
            return bitmap;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException)
        {
            return null;
        }
    }
}
