using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnyPS5Gui.Core.Components;

public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

/// <summary>An AnyPS5 release and the two files of it the app needs on Windows.</summary>
public sealed record ReleaseInfo(string Tag, DateTimeOffset? PublishedAt, ReleaseAsset? Relinker, ReleaseAsset? Libraries)
{
    /// <summary>The GitHub repository the release is from: AnyPS5's own, unless the user added another source.</summary>
    public string Repository { get; init; } = ReleaseClient.Repository;

    public bool IsOfficial => Repository.Equals(ReleaseClient.Repository, StringComparison.OrdinalIgnoreCase);

    /// <summary>What the list of versions shows: a build from elsewhere carries the name of where it is from.</summary>
    public string Label => IsOfficial ? Tag : $"{Tag}  ·  {Repository}";

    public bool IsComplete => Relinker is not null && Libraries is not null;
    public long DownloadSize => (Relinker?.Size ?? 0) + (Libraries?.Size ?? 0);
}

/// <summary>Reads the release list of a GitHub repository, AnyPS5's own by default, from the public GitHub API.</summary>
public sealed partial class ReleaseClient(HttpClient http)
{
    public const string Repository = "boykopovar/AnyPS5";

    /// <summary>An <c>owner/name</c> that can go into a GitHub URL as it is.</summary>
    public static bool IsValidRepository(string? repository) =>
        repository is not null && RepositoryPattern().IsMatch(repository) && !repository.Contains("..");

    /// <summary>GitHub rejects requests without a user agent. The name identifies the app, not the user.</summary>
    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AnyPS5-Gui");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>How long GitHub is given to answer with a release list.</summary>
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Newest first, drafts and prereleases left out. An answer that does not arrive in time is a failed
    /// request, an <see cref="HttpRequestException"/>: only the caller's own token cancels.
    /// </summary>
    public async Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(string repository = Repository,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidRepository(repository)) throw new ArgumentException($"'{repository}' is not a GitHub repository name.", nameof(repository));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ResponseTimeout);
        string json;
        try
        {
            json = await http.GetStringAsync($"https://api.github.com/repos/{repository}/releases?per_page=15", timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException($"GitHub did not answer within {ResponseTimeout.TotalSeconds:0.#} seconds.", e);
        }
        return Parse(json, repository);
    }

    public static IReadOnlyList<ReleaseInfo> Parse(string json, string repository = Repository)
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
            releases.Add(new ReleaseInfo(tag, published, relinker, libraries) { Repository = repository });
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

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9._-]+$")]
    private static partial Regex RepositoryPattern();
}
