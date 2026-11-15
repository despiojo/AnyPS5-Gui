using System.Text.Json;

namespace AnyPS5Gui.Core.Games;

/// <summary>The few fields of a title's <c>sce_sys/param.json</c> the app shows.</summary>
public sealed record ParamJson(string? TitleId, string? TitleName, string? ContentVersion)
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Null when the file is missing, unreadable or not a JSON object.</summary>
    public static ParamJson? Read(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ParamJson? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, Options);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            return new ParamJson(Text(root, "titleId"), FindTitleName(root), Text(root, "contentVersion"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Same order AnyPS5 uses: the default language, then en-US, then the first language present.
    private static string? FindTitleName(JsonElement root)
    {
        if (!root.TryGetProperty("localizedParameters", out var localized) ||
            localized.ValueKind != JsonValueKind.Object)
            return null;

        var defaultLanguage = Text(localized, "defaultLanguage") ?? Text(root, "defaultLanguage");
        foreach (var language in new[] { defaultLanguage, "en-US" })
        {
            if (language is not null && localized.TryGetProperty(language, out var entry) &&
                entry.ValueKind == JsonValueKind.Object && Text(entry, "titleName") is { } name)
                return name;
        }

        foreach (var property in localized.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object && Text(property.Value, "titleName") is { } name)
                return name;
        }
        return null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
