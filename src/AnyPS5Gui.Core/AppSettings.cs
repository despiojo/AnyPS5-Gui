namespace AnyPS5Gui.Core;

public sealed class AppSettings
{
    /// <summary>Folder that receives one subfolder per converted game. Empty: next to the game's own folder.</summary>
    public string? OutputRoot { get; set; }

    /// <summary>
    /// Name of the downloaded AnyPS5 version in use: the tag of its release, such as <c>v0.1.5</c>, followed
    /// by <c>@owner~name</c> when it is a build from another repository.
    /// </summary>
    public string? ActiveVersion { get; set; }

    /// <summary>
    /// Another GitHub repository (<c>owner/name</c>) whose releases are listed with AnyPS5's own, such as a
    /// fork that publishes builds with changes AnyPS5 has not released yet.
    /// </summary>
    public string? ExtraReleaseSource { get; set; }

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

    /// <summary>
    /// Every conversion and every game run writes its output to a text file. This many of the newest
    /// are kept; 0 writes none.
    /// </summary>
    public int LogFilesToKeep { get; set; } = 10;
}

public enum AppTheme { System, Light, Dark }
