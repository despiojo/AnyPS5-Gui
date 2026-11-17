namespace AnyPS5Gui.Core.Components;

/// <summary>What a check for versions comes back with: the releases to choose from, and the one a download takes when none was chosen.</summary>
public sealed record ReleaseOffer(IReadOnlyList<ReleaseInfo> Releases, ReleaseInfo? Default)
{
    /// <summary>
    /// AnyPS5's own releases come first and its newest is the default, so a build from elsewhere is never
    /// downloaded without being asked for. With <paramref name="preferOther"/> the default is
    /// <paramref name="latestOther"/>, the release the other repository marks as its latest, and nothing
    /// when there is none to use: the order of that repository's list says nothing about which build is
    /// meant to be taken, and AnyPS5's newest is not what was asked for.
    /// </summary>
    public static ReleaseOffer Create(IEnumerable<ReleaseInfo> official, IEnumerable<ReleaseInfo> other,
        bool preferOther = false, ReleaseInfo? latestOther = null)
    {
        var releases = official.Concat(other).Where(release => release.IsComplete).ToList();
        if (!preferOther) return new ReleaseOffer(releases, releases.FirstOrDefault());
        if (latestOther is not { IsComplete: true }) return new ReleaseOffer(releases, null);

        // The one in the list when it is there: the list of a repository only reaches so far back.
        var listed = releases.Find(release =>
            release.VersionName.Equals(latestOther.VersionName, StringComparison.OrdinalIgnoreCase));
        if (listed is null) releases.Add(listed = latestOther);
        return new ReleaseOffer(releases, listed);
    }
}
