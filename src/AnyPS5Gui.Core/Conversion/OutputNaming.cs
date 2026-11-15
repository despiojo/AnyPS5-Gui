using System.Text.RegularExpressions;
using AnyPS5Gui.Core.Games;

namespace AnyPS5Gui.Core.Conversion;

/// <summary>Default names for what a conversion writes.</summary>
public static partial class OutputNaming
{
    /// <summary>The converted executable is named after the title id, so it is recognisable in Task Manager.</summary>
    public static string ExecutableName(GameInfo game) =>
        game.Param?.TitleId is { } id && SafeId().IsMatch(id) ? id + ".exe" : "app.exe";

    /// <summary>
    /// With an output root: <c>root\Title [ID]</c>. Without one, a sibling of the game folder, which
    /// keeps the output on the same drive so the game files can be hard-linked instead of copied.
    /// </summary>
    public static string DefaultOutputDirectory(GameInfo game, string? outputRoot)
    {
        var folder = Path.TrimEndingDirectorySeparator(game.Folder);
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            var parent = Path.GetDirectoryName(folder);
            // A game at the root of a drive has no parent to sit beside.
            return parent is null
                ? Path.Combine(folder, "..", "AnyPS5")
                : Path.Combine(parent, Path.GetFileName(folder) + " - AnyPS5");
        }

        var name = game.DisplayName;
        if (game.Param?.TitleId is { } id && !name.Contains(id, StringComparison.OrdinalIgnoreCase)) name += $" [{id}]";
        return Path.Combine(outputRoot, SanitizeFileName(name));
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? ' ' : c).ToArray());
        clean = Spaces().Replace(clean, " ").Trim().TrimEnd('.');
        return clean.Length == 0 ? "Game" : clean;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex SafeId();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
