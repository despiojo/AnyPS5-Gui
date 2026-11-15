using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnyPS5Gui.Core;

/// <summary>Small JSON documents kept on disk: the settings and the game list.</summary>
public static class JsonFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Reads the document, or returns a new one when the file does not exist. A file that cannot be
    /// parsed is set aside as <c>.corrupt</c> instead of being overwritten by the next save.
    /// </summary>
    public static T Load<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options) ?? new T();
        }
        catch (JsonException)
        {
            File.Move(path, path + ".corrupt", overwrite: true);
            return new T();
        }
    }

    /// <summary>Writes through a temporary file, so a crash mid-write never leaves half a document.</summary>
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary))
            JsonSerializer.Serialize(stream, value, Options);
        File.Move(temporary, path, overwrite: true);
    }
}
