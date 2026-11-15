using System.Text.Json.Serialization;
using AnyPS5Gui.Core.Launch;

namespace AnyPS5Gui.Core.Library;

/// <summary>A converted game the app can launch.</summary>
public sealed class GameEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string? TitleId { get; set; }
    public string? ContentVersion { get; set; }
    public string ExecutablePath { get; set; } = "";
    public string? SourceFolder { get; set; }
    public string? ComponentVersion { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }
    public LaunchSettings Launch { get; set; } = new();

    [JsonIgnore]
    public string Directory => Path.GetDirectoryName(ExecutablePath) ?? "";

    /// <summary>The game's own icon, which the conversion places under <c>app0/</c>.</summary>
    [JsonIgnore]
    public string? IconPath
    {
        get
        {
            var icon = Path.Combine(Directory, "app0", "sce_sys", "icon0.png");
            return File.Exists(icon) ? icon : null;
        }
    }
}

/// <summary>The list of converted games, kept in <c>games.json</c>.</summary>
public sealed class GameLibrary(string file)
{
    private sealed class Document
    {
        public List<GameEntry> Games { get; set; } = [];
    }

    private Document _document = new();

    public IReadOnlyList<GameEntry> Games => _document.Games;

    public void Load() => _document = JsonFile.Load<Document>(file);

    public void Save() => JsonFile.Save(file, _document);

    /// <summary>
    /// Adds a game, or refreshes the entry that already points at the same executable, keeping its
    /// launch settings: converting a game again should not reset how it is started.
    /// </summary>
    public GameEntry AddOrUpdate(GameEntry entry)
    {
        var existing = _document.Games.FirstOrDefault(game =>
            string.Equals(Path.GetFullPath(game.ExecutablePath), Path.GetFullPath(entry.ExecutablePath),
                StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _document.Games.Add(entry);
            Save();
            return entry;
        }

        existing.Title = entry.Title;
        existing.TitleId = entry.TitleId;
        existing.ContentVersion = entry.ContentVersion;
        existing.SourceFolder = entry.SourceFolder;
        existing.ComponentVersion = entry.ComponentVersion;
        existing.ConvertedAt = entry.ConvertedAt;
        Save();
        return existing;
    }

    /// <summary>Forgets the game. Its files stay on disk.</summary>
    public void Remove(GameEntry entry)
    {
        if (_document.Games.Remove(entry)) Save();
    }
}
