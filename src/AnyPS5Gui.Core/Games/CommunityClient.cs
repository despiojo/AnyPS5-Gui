using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AnyPS5Gui.Core.Components;

namespace AnyPS5Gui.Core.Games;

/// <summary>A game bring-up thread from AnyPS5's GitHub Discussions.</summary>
public sealed record CommunityDiscussion(
    int Number,
    string Title,
    string Url,
    int Comments,
    DateTimeOffset? UpdatedAt,
    string? Status,
    IReadOnlyList<string> MentionedForks);

/// <summary>A GitHub fork or community repository targeting AnyPS5 or a specific game.</summary>
public sealed record CommunityFork(
    string Repository,
    string? Description,
    int Stars,
    DateTimeOffset? UpdatedAt)
{
    public string Url => $"https://github.com/{Repository}";
}

/// <summary>What GitHub's discussions and repository search report for a game.</summary>
public sealed record CommunityReport(
    IReadOnlyList<CommunityDiscussion> Discussions,
    IReadOnlyList<CommunityFork> Forks);

/// <summary>
/// Queries public GitHub discussions and repositories on demand for community bring-up reports and
/// game-specific forks.
/// </summary>
public sealed partial class CommunityClient(HttpClient http)
{
    private static readonly string[] IgnoredOwners =
    [
        "boykopovar", "despiojo", "Lenny4250gh", "ahmad3a4", "awake-devel", "anyplaynet",
    ];

    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public async Task<CommunityReport> LookupGameAsync(string? titleId, string? titleName,
        CancellationToken cancellationToken = default)
    {
        var profile = GameCatalog.Find(titleId, titleName);
        var discussions = new List<CommunityDiscussion>();

        if (profile?.DiscussionNumber is { } knownNumber)
        {
            var json = await ReadAsync(
                $"https://api.github.com/repos/{ReleaseClient.Repository}/discussions/{knownNumber}",
                cancellationToken, missingIsNothing: true).ConfigureAwait(false);
            if (json is not null && ParseSingleDiscussion(json) is { } direct)
                discussions.Add(direct);
        }

        if (discussions.Count == 0 && (!string.IsNullOrWhiteSpace(titleId) || !string.IsNullOrWhiteSpace(titleName)))
        {
            var listJson = await ReadAsync(
                $"https://api.github.com/repos/{ReleaseClient.Repository}/discussions?per_page=30",
                cancellationToken, missingIsNothing: true).ConfigureAwait(false);
            if (listJson is not null)
                discussions.AddRange(FilterDiscussions(ParseDiscussions(listJson), titleId, titleName));
        }

        var searchTerm = PickSearchTerm(profile, titleId, titleName);
        var forks = new List<CommunityFork>();
        if (searchTerm is not null)
        {
            var query = Uri.EscapeDataString($"AnyPS5 {searchTerm}");
            var searchJson = await ReadAsync(
                $"https://api.github.com/search/repositories?q={query}&sort=updated&per_page=10",
                cancellationToken, missingIsNothing: true).ConfigureAwait(false);
            if (searchJson is not null)
                forks.AddRange(ParseForkSearch(searchJson));
        }

        // Include forks mentioned in the discussion body or known in the built-in catalog for this game.
        var knownRepos = new HashSet<string>(forks.Select(f => f.Repository), StringComparer.OrdinalIgnoreCase);
        var extras = discussions.SelectMany(d => d.MentionedForks)
            .Concat(profile?.RecommendedForks ?? []);
        foreach (var repo in extras)
        {
            if (ReleaseClient.IsValidRepository(repo) && !IsIgnoredRepository(repo) && knownRepos.Add(repo))
                forks.Add(new CommunityFork(repo, null, 0, null));
        }

        return new CommunityReport(discussions, forks);
    }

    public static CommunityDiscussion? ParseSingleDiscussion(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ReadDiscussion(document.RootElement);
    }

    public static IReadOnlyList<CommunityDiscussion> ParseDiscussions(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
        var list = new List<CommunityDiscussion>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (ReadDiscussion(element) is { } discussion) list.Add(discussion);
        }
        return list;
    }

    public static IReadOnlyList<CommunityDiscussion> FilterDiscussions(
        IEnumerable<CommunityDiscussion> discussions, string? titleId, string? titleName)
    {
        var id = string.IsNullOrWhiteSpace(titleId) ? null : titleId.Trim();
        var name = string.IsNullOrWhiteSpace(titleName) ? null : NormalizeAscii(titleName.Trim());
        var profile = GameCatalog.Find(id, name);

        return discussions.Where(d =>
        {
            if (id is not null && d.Title.Contains(id, StringComparison.OrdinalIgnoreCase)) return true;
            var title = NormalizeAscii(d.Title);
            if (name is not null && title.Contains(name, StringComparison.OrdinalIgnoreCase)) return true;
            if (profile is not null && profile.TitleKeywords.Any(k =>
                    title.Contains(NormalizeAscii(k), StringComparison.OrdinalIgnoreCase)))
                return true;
            return false;
        }).ToList();
    }

    public static IReadOnlyList<CommunityFork> ParseForkSearch(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var forks = new List<CommunityFork>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (Text(item, "full_name") is not { } repo || !ReleaseClient.IsValidRepository(repo)) continue;
            if (IsIgnoredRepository(repo)) continue;

            var description = Text(item, "description");
            var stars = item.TryGetProperty("stargazers_count", out var s) && s.TryGetInt32(out var count) ? count : 0;
            DateTimeOffset? updated = item.TryGetProperty("pushed_at", out var p) &&
                                      p.ValueKind == JsonValueKind.String &&
                                      p.TryGetDateTimeOffset(out var parsed) ? parsed
                : item.TryGetProperty("updated_at", out var u) &&
                  u.ValueKind == JsonValueKind.String &&
                  u.TryGetDateTimeOffset(out var parsedUpdated) ? parsedUpdated
                : null;
            forks.Add(new CommunityFork(repo, description, stars, updated));
        }
        return forks;
    }

    /// <summary>Extracts the "Furthest point reached" or status line from a bring-up discussion body.</summary>
    public static string? ExtractStatus(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var match = StatusLine().Match(body);
        if (!match.Success) return null;
        var value = match.Groups[1].Value.Trim().Trim('*', '_', '`', ' ', '-', ':');
        return value.Length > 0 ? value : null;
    }

    /// <summary>Finds GitHub <c>owner/repo</c> references to AnyPS5 forks inside a discussion text.</summary>
    public static IReadOnlyList<string> ExtractMentionedForks(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ForkReference().Matches(body))
        {
            var repo = match.Groups[1].Value;
            if (ReleaseClient.IsValidRepository(repo) && !IsIgnoredRepository(repo) && seen.Add(repo))
                found.Add(repo);
        }
        return found;
    }

    private static CommunityDiscussion? ReadDiscussion(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty("number", out var n) || !n.TryGetInt32(out var number)) return null;
        if (Text(element, "title") is not { } title || Text(element, "html_url") is not { } url) return null;

        var comments = element.TryGetProperty("comments", out var c) && c.TryGetInt32(out var count) ? count : 0;
        DateTimeOffset? updated = element.TryGetProperty("updated_at", out var u) &&
                                  u.ValueKind == JsonValueKind.String &&
                                  u.TryGetDateTimeOffset(out var parsed) ? parsed : null;
        var body = Text(element, "body");
        return new CommunityDiscussion(
            number,
            title,
            url,
            comments,
            updated,
            ExtractStatus(body),
            ExtractMentionedForks(body));
    }

    private static string? PickSearchTerm(GameProfile? profile, string? titleId, string? titleName)
    {
        if (profile is not null)
        {
            var shortKeyword = profile.TitleKeywords
                .OrderBy(k => k.Length)
                .FirstOrDefault();
            if (shortKeyword is not null) return NormalizeAscii(shortKeyword);
        }
        if (!string.IsNullOrWhiteSpace(titleName)) return NormalizeAscii(titleName.Trim());
        if (!string.IsNullOrWhiteSpace(titleId)) return titleId.Trim();
        return null;
    }

    private static bool IsIgnoredRepository(string repository)
    {
        if (repository.Equals(ReleaseClient.Repository, StringComparison.OrdinalIgnoreCase)) return true;
        var slash = repository.IndexOf('/');
        if (slash < 0) return true;
        var owner = repository[..slash];
        var name = repository[(slash + 1)..];
        if (IgnoredOwners.Any(ignored => owner.Equals(ignored, StringComparison.OrdinalIgnoreCase))) return true;
        if (name.Contains("Gui", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Launcher", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Tracker", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("roadmap", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private async Task<string?> ReadAsync(string url, CancellationToken cancellationToken, bool missingIsNothing = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ResponseTimeout);
        try
        {
            using var response = await http.GetAsync(url, timeout.Token).ConfigureAwait(false);
            if (missingIsNothing && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.UnprocessableEntity)
                return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException($"GitHub did not answer within {ResponseTimeout.TotalSeconds:0.#} seconds.", e);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string NormalizeAscii(string text) =>
        text.Replace('ō', 'o').Replace('Ō', 'O');

    [GeneratedRegex(@"(?:Furthest point reached|Status)\s*[:\-]\s*([^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex StatusLine();

    [GeneratedRegex(@"(?:https://github\.com/|\b)([A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9._-]*AnyPS5[A-Za-z0-9._-]*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForkReference();
}
