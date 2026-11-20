using AnyPS5Gui.Core.Components;

namespace AnyPS5Gui.Core.Games;

/// <summary>A community fork of AnyPS5 known to target specific games or backends.</summary>
public sealed record KnownFork(string Repository, string SummaryKey, IReadOnlyList<string> TitleIds)
{
    public string Url => $"https://github.com/{Repository}";
}

/// <summary>
/// Community knowledge about a PS5 game: which forks target it, which discussion thread tracks its
/// bring-up, and whether particular launch settings help.
/// </summary>
public sealed record GameProfile(
    string Title,
    IReadOnlyList<string> TitleIds,
    IReadOnlyList<string> TitleKeywords,
    string SummaryKey,
    int? DiscussionNumber,
    IReadOnlyList<string> RecommendedForks,
    bool RecommendMimalloc = false)
{
    public string DiscussionUrl => DiscussionNumber is { } number
        ? $"https://github.com/{ReleaseClient.Repository}/discussions/{number}"
        : GameCatalog.DiscussionSearchUrl(TitleIds.FirstOrDefault(), Title);
}

/// <summary>
/// Built-in catalog of community forks and game-specific profiles (such as Ghost of Yōtei), so the app
/// can suggest the right fork and link straight to the game's bring-up thread without a network call.
/// </summary>
public static class GameCatalog
{
    public static IReadOnlyList<KnownFork> Forks { get; } =
    [
        new("Izamaru/AnyPS5-Yotei", "Settings_ForkIzamaruYotei", ["PPSA26344"]),
        new("simnJS/AnyPS5", "Settings_ForkSimnJsAnyKyty", ["PPSA26344"]),
        new("Silverash-i8/AnyPS5-fork", "Settings_ForkSilverash", []),
    ];

    public static IReadOnlyList<GameProfile> Profiles { get; } =
    [
        new(
            Title: "Ghost of Yōtei",
            TitleIds: ["PPSA26344"],
            TitleKeywords: ["Ghost of Yotei", "Ghost of Yōtei", "Yotei", "Yōtei"],
            SummaryKey: "Convert_AdviceGhostOfYotei",
            DiscussionNumber: 675,
            RecommendedForks: ["Izamaru/AnyPS5-Yotei", "simnJS/AnyPS5"],
            RecommendMimalloc: true),
    ];

    /// <summary>Finds the community profile for an inspected game folder, by title ID first and title name second.</summary>
    public static GameProfile? Find(GameInfo? game) =>
        game is null ? null : Find(game.Param?.TitleId, game.DisplayName);

    public static GameProfile? Find(string? titleId, string? titleName = null)
    {
        if (!string.IsNullOrWhiteSpace(titleId))
        {
            var id = titleId.Trim();
            var byId = Profiles.FirstOrDefault(profile =>
                profile.TitleIds.Any(candidate => candidate.Equals(id, StringComparison.OrdinalIgnoreCase)));
            if (byId is not null) return byId;
        }

        if (!string.IsNullOrWhiteSpace(titleName))
        {
            var name = titleName.Trim();
            var byName = Profiles.FirstOrDefault(profile =>
                profile.TitleKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
            if (byName is not null) return byName;
        }

        return null;
    }

    /// <summary>
    /// Picks the newest installed version that comes from a fork recommended for the game, or whose
    /// repository name matches one of the game's keywords (such as <c>yotei</c>). Null when no
    /// game-specific build is installed.
    /// </summary>
    public static string? RecommendInstalledVersion(GameInfo? game, IEnumerable<string> installedVersions) =>
        game is null ? null : RecommendInstalledVersion(game.Param?.TitleId, game.DisplayName, installedVersions);

    public static string? RecommendInstalledVersion(string? titleId, string? titleName, IEnumerable<string> installedVersions)
    {
        var profile = Find(titleId, titleName);
        var versions = installedVersions.ToList();
        if (versions.Count == 0) return null;

        if (profile is not null)
        {
            foreach (var fork in profile.RecommendedForks)
            {
                var suffix = "@" + fork.Replace('/', '~');
                var exact = versions.FirstOrDefault(v => v.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                if (exact is not null) return exact;
            }

            foreach (var keyword in profile.TitleKeywords)
            {
                var ASCIIKeyword = NormalizeAscii(keyword);
                var match = versions.FirstOrDefault(v =>
                    v.Contains('@') && NormalizeAscii(v[(v.IndexOf('@') + 1)..]).Contains(ASCIIKeyword, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return match;
            }
        }

        if (!string.IsNullOrWhiteSpace(titleId))
        {
            var byId = versions.FirstOrDefault(v =>
                v.Contains('@') && v[(v.IndexOf('@') + 1)..].Contains(titleId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byId is not null) return byId;
        }

        return null;
    }

    /// <summary>
    /// Direct URL to the game's bring-up discussion when known, or a search of AnyPS5's discussions for
    /// its title ID or title name.
    /// </summary>
    public static string DiscussionSearchUrl(string? titleId, string? titleName = null)
    {
        if (Find(titleId, titleName) is { DiscussionNumber: { } number })
            return $"https://github.com/{ReleaseClient.Repository}/discussions/{number}";

        var query = !string.IsNullOrWhiteSpace(titleId) ? titleId.Trim()
            : !string.IsNullOrWhiteSpace(titleName) ? titleName.Trim()
            : null;
        return query is null
            ? $"https://github.com/{ReleaseClient.Repository}/discussions/categories/game-bring-up"
            : $"https://github.com/{ReleaseClient.Repository}/discussions?discussions_q={Uri.EscapeDataString(query)}";
    }

    private static string NormalizeAscii(string text) =>
        text.Replace('ō', 'o').Replace('Ō', 'O');
}
