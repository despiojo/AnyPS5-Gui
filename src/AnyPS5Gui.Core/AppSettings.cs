namespace AnyPS5Gui.Core;

public sealed class AppSettings
{
    /// <summary>Folder that receives one subfolder per converted game. Empty: next to the game's own folder.</summary>
    public string? OutputRoot { get; set; }

    /// <summary>Tag of the downloaded AnyPS5 release in use, such as <c>v0.1.5</c>.</summary>
    public string? ActiveVersion { get; set; }

    /// <summary>A relinker to use instead of the downloaded one, for self-built AnyPS5.</summary>
    public string? CustomRelinkerPath { get; set; }

    /// <summary>A folder of system libraries to use instead of the downloaded ones.</summary>
    public string? CustomLibsPath { get; set; }

    /// <summary>UI language tag (<c>en-US</c>, <c>es-ES</c>). Empty: follow Windows.</summary>
    public string? Language { get; set; }

    public AppTheme Theme { get; set; }

    /// <summary>Hard-link game files into the output instead of copying them.</summary>
    public bool LinkFiles { get; set; } = true;

    public string? LastGameFolder { get; set; }
}

public enum AppTheme { System, Light, Dark }
