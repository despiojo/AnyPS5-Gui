using System.Text.Json;

namespace AnyPS5Gui.Core.Components;

public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

/// <summary>An AnyPS5 release and the two files of it the app needs on Windows.</summary>
public sealed record ReleaseInfo(string Tag, DateTimeOffset? PublishedAt, ReleaseAsset? Relinker, ReleaseAsset? Libraries)
{
    public bool IsComplete => Relinker is not null && Libraries is not null;
    public long DownloadSize => (Relinker?.Size ?? 0) + (Libraries?.Size ?? 0);
}

/// <summary>Reads the release list of the AnyPS5 repository from the public GitHub API.</summary>
public sealed class ReleaseClient(HttpClient http)
{
    public const string Repository = "boykopovar/AnyPS5";
    private const string ReleasesUrl = $"https://api.github.com/repos/{Repository}/releases?per_page=15";

    /// <summary>GitHub rejects requests without a user agent. The name identifies the app, not the user.</summary>
    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AnyPS5-Gui");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>Newest first, drafts and prereleases left out.</summary>
    public async Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var json = await http.GetStringAsync(ReleasesUrl, timeout.Token).ConfigureAwait(false);
        return Parse(json);
    }

    public static IReadOnlyList<ReleaseInfo> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<ReleaseInfo>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (Flag(release, "draft") || Flag(release, "prerelease")) continue;
            if (Text(release, "tag_name") is not { } tag || !ComponentStore.IsValidTag(tag)) continue;

            ReleaseAsset? relinker = null, libraries = null;
            if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (ReadAsset(asset) is not { } read) continue;
                    if (read.Name.Equals($"relinker-{tag}.exe", StringComparison.OrdinalIgnoreCase)) relinker = read;
                    else if (read.Name.Equals($"prx-windows-{tag}.zip", StringComparison.OrdinalIgnoreCase)) libraries = read;
                }
            }

            DateTimeOffset? published = release.TryGetProperty("published_at", out var date) &&
                                        date.ValueKind == JsonValueKind.String &&
                                        date.TryGetDateTimeOffset(out var parsed) ? parsed : null;
            releases.Add(new ReleaseInfo(tag, published, relinker, libraries));
        }
        return releases;
    }

    private static ReleaseAsset? ReadAsset(JsonElement asset)
    {
        if (Text(asset, "name") is not { } name || Text(asset, "browser_download_url") is not { } url) return null;
        var size = asset.TryGetProperty("size", out var value) && value.TryGetInt64(out var bytes) ? bytes : 0;

        // "sha256:<hex>" on releases published since GitHub started recording digests.
        var digest = Text(asset, "digest");
        var sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest["sha256:".Length..]
            : null;
        return new ReleaseAsset(name, url, size, sha256);
    }

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
