namespace AnyPS5Gui.Core;

/// <summary>Where the app keeps its own files. Everything lives under one folder so it can be moved or deleted as a whole.</summary>
public sealed class AppPaths(string root)
{
    public static AppPaths Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnyPS5Gui"));

    public string Root { get; } = root;
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string LibraryFile => Path.Combine(Root, "games.json");
    public string ComponentsDirectory => Path.Combine(Root, "components");
    public string LogsDirectory => Path.Combine(Root, "logs");
}
