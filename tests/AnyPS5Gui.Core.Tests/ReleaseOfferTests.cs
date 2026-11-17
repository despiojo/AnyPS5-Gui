using AnyPS5Gui.Core.Components;

namespace AnyPS5Gui.Core.Tests;

public class ReleaseOfferTests
{
    private const string Fork = "someone/AnyPS5";

    private static ReleaseInfo Release(string tag, string repository = ReleaseClient.Repository, bool complete = true) =>
        new(tag, null,
            new ReleaseAsset($"relinker-{tag}.exe", $"https://example.test/{tag}.exe", 1, null),
            complete ? new ReleaseAsset($"prx-windows-{tag}.zip", $"https://example.test/{tag}.zip", 1, null) : null)
        {
            Repository = repository,
        };

    private static readonly ReleaseInfo[] Official = [Release("v0.1.5"), Release("v0.1.4", complete: false), Release("v0.1.3")];

    // In the order GitHub lists them, which is not the order they were published in.
    private static readonly ReleaseInfo[] FromFork =
        [Release("vmain-a6606d5-pr19326", Fork), Release("vmain-a72186d", Fork), Release("vmain-a6606d5", Fork)];

    private static IEnumerable<string> Names(ReleaseOffer offer) => offer.Releases.Select(release => release.VersionName);

    [Fact]
    public void Without_a_preference_the_default_is_the_newest_of_AnyPS5()
    {
        var offer = ReleaseOffer.Create(Official, FromFork);

        Assert.Equal(
            ["v0.1.5", "v0.1.3", "vmain-a6606d5-pr19326@someone~AnyPS5", "vmain-a72186d@someone~AnyPS5", "vmain-a6606d5@someone~AnyPS5"],
            Names(offer));
        Assert.Equal("v0.1.5", offer.Default?.VersionName);
    }

    [Fact]
    public void The_latest_release_of_the_other_repository_is_not_the_default_unless_it_is_preferred()
    {
        var offer = ReleaseOffer.Create(Official, FromFork, latestOther: Release("vmain-a72186d", Fork));

        Assert.Equal("v0.1.5", offer.Default?.VersionName);
    }

    [Fact]
    public void A_preferred_repository_makes_its_latest_release_the_default_and_leaves_the_list_as_it_was()
    {
        var offer = ReleaseOffer.Create(Official, FromFork, preferOther: true, latestOther: Release("vmain-a72186d", Fork));

        Assert.Equal("vmain-a72186d@someone~AnyPS5", offer.Default?.VersionName);
        Assert.Equal(
            ["v0.1.5", "v0.1.3", "vmain-a6606d5-pr19326@someone~AnyPS5", "vmain-a72186d@someone~AnyPS5", "vmain-a6606d5@someone~AnyPS5"],
            Names(offer));
    }

    [Fact]
    public void A_latest_release_that_the_list_does_not_reach_is_added_to_it()
    {
        var offer = ReleaseOffer.Create(Official, FromFork, preferOther: true, latestOther: Release("vmain-0000000", Fork));

        Assert.Equal("vmain-0000000@someone~AnyPS5", offer.Default?.VersionName);
        Assert.Equal("vmain-0000000@someone~AnyPS5", Names(offer).Last());
        Assert.Equal(6, offer.Releases.Count);
    }

    [Fact]
    public void A_preferred_repository_without_a_latest_release_gives_no_default_rather_than_another_build()
    {
        var offer = ReleaseOffer.Create(Official, FromFork, preferOther: true, latestOther: null);

        Assert.Null(offer.Default);
        Assert.Equal(5, offer.Releases.Count);
    }

    [Fact]
    public void A_latest_release_without_the_windows_files_is_neither_the_default_nor_listed()
    {
        var offer = ReleaseOffer.Create(Official, FromFork, preferOther: true,
            latestOther: Release("vmain-0000000", Fork, complete: false));

        Assert.Null(offer.Default);
        Assert.DoesNotContain("vmain-0000000@someone~AnyPS5", Names(offer));
    }

    [Fact]
    public void Nothing_to_choose_from_gives_no_default()
    {
        var offer = ReleaseOffer.Create([], []);

        Assert.Empty(offer.Releases);
        Assert.Null(offer.Default);
    }
}
