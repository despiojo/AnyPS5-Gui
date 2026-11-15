using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AnyPS5Gui;

/// <summary>Text from Strings\&lt;language&gt;\Resources.resw, for what is not set through x:Uid.</summary>
public static class Loc
{
    private static readonly ResourceLoader Loader = new();

    /// <summary>A missing key shows as the key itself, which is easier to spot and report than a blank.</summary>
    public static string Get(string key)
    {
        try
        {
            var text = Loader.GetString(key);
            return string.IsNullOrEmpty(text) ? key : text;
        }
        catch (Exception)
        {
            return key;
        }
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
}
