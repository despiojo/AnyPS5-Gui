using Microsoft.UI.Xaml;

namespace AnyPS5Gui;

public static class LogStyles
{
    /// <summary>
    /// The text style of a log line, from App.xaml. A style rather than a brush: its colours are
    /// theme resources, so they follow the theme chosen in the app and not only the one of Windows.
    /// </summary>
    public static Style For(LogKind kind) => (Style)Application.Current.Resources[kind switch
    {
        LogKind.Error => "LogErrorStyle",
        LogKind.Warning => "LogWarningStyle",
        LogKind.Success => "LogSuccessStyle",
        LogKind.Info => "LogInfoStyle",
        _ => "MonoTextStyle",
    }];
}
