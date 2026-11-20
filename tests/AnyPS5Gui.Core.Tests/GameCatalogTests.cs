using System.Net;
using System.Text;
using AnyPS5Gui.Core.Games;

namespace AnyPS5Gui.Core.Tests;

public class GameCatalogTests
{
    [Theory]
    [InlineData("PPSA26344", null)]
    [InlineData("ppsa26344", "Anything")]
    [InlineData(null, "Ghost of Yōtei")]
    [InlineData(null, "Ghost of Yotei - Dump")]
    public void Ghost_of_Yotei_is_matched_by_title_id_or_name(string? titleId, string? titleName)
    {
        var profile = GameCatalog.Find(titleId, titleName);

        Assert.NotNull(profile);
        Assert.Equal("Ghost of Yōtei", profile.Title);
        Assert.Equal(675, profile.DiscussionNumber);
        Assert.Equal("https://github.com/boykopovar/AnyPS5/discussions/675", profile.DiscussionUrl);
        Assert.Contains("Izamaru/AnyPS5-Yotei", profile.RecommendedForks);
        Assert.Contains("simnJS/AnyPS5", profile.RecommendedForks);
        Assert.True(profile.RecommendMimalloc);
    }

    [Fact]
    public void Unknown_games_have_no_built_in_profile_and_use_discussion_search()
    {
        Assert.Null(GameCatalog.Find("PPSA99999", "Some Other Game"));
        Assert.Equal("https://github.com/boykopovar/AnyPS5/discussions?discussions_q=PPSA99999",
            GameCatalog.DiscussionSearchUrl("PPSA99999", "Some Other Game"));
        Assert.Equal("https://github.com/boykopovar/AnyPS5/discussions?discussions_q=Some%20Other%20Game",
            GameCatalog.DiscussionSearchUrl(null, "Some Other Game"));
        Assert.Equal("https://github.com/boykopovar/AnyPS5/discussions/categories/game-bring-up",
            GameCatalog.DiscussionSearchUrl(null, null));
    }

    [Fact]
    public void Recommended_installed_version_prefers_a_fork_matching_the_game()
    {
        var installed = new[]
        {
            "v0.1.5",
            "v0.1.5@someone~AnyPS5",
            "vmain-a72186d@Izamaru~AnyPS5-Yotei",
        };

        Assert.Equal("vmain-a72186d@Izamaru~AnyPS5-Yotei",
            GameCatalog.RecommendInstalledVersion("PPSA26344", "Ghost of Yōtei", installed));

        var customYoteiFork = new[] { "v0.1.5", "v0.1.5@another~AnyPS5-yotei-test" };
        Assert.Equal("v0.1.5@another~AnyPS5-yotei-test",
            GameCatalog.RecommendInstalledVersion("PPSA26344", "Ghost of Yōtei", customYoteiFork));

        Assert.Null(GameCatalog.RecommendInstalledVersion("PPSA26344", "Ghost of Yōtei", ["v0.1.5", "v0.1.5@someone~AnyPS5"]));
        Assert.Null(GameCatalog.RecommendInstalledVersion("PPSA99999", "Other Game", installed));
    }

    [Fact]
    public async Task Community_client_reads_bring_up_discussion_and_discovers_forks()
    {
        const string discussion675 = """
            {
              "number": 675,
              "title": "Ghost of Yōtei (PPSA26344)",
              "html_url": "https://github.com/boykopovar/AnyPS5/discussions/675",
              "comments": 42,
              "updated_at": "2026-11-19T18:30:00Z",
              "body": "Furthest point reached: In-game (open world via AnyKyty)\nTested with https://github.com/simnJS/AnyPS5 and custom/AnyPS5-yotei."
            }
            """;

        const string forkSearch = """
            {
              "items": [
                {
                  "full_name": "Izamaru/AnyPS5-Yotei",
                  "description": "AnyPS5 fork for Ghost of Yotei",
                  "stargazers_count": 15,
                  "pushed_at": "2026-11-18T12:00:00Z"
                },
                {
                  "full_name": "Izamaru/AnyPS5-Tracker",
                  "description": "Tracker utility",
                  "stargazers_count": 5,
                  "pushed_at": "2026-11-18T12:00:00Z"
                }
              ]
            }
            """;

        using var http = new HttpClient(new FakeGitHub(new()
        {
            ["https://api.github.com/repos/boykopovar/AnyPS5/discussions/675"] = discussion675,
            ["https://api.github.com/search/repositories?q=AnyPS5%20Yotei&sort=updated&per_page=10"] = forkSearch,
        }));
        var client = new CommunityClient(http);

        var report = await client.LookupGameAsync("PPSA26344", "Ghost of Yōtei", TestContext.Current.CancellationToken);

        var discussion = Assert.Single(report.Discussions);
        Assert.Equal(675, discussion.Number);
        Assert.Equal("Ghost of Yōtei (PPSA26344)", discussion.Title);
        Assert.Equal(42, discussion.Comments);
        Assert.Equal("In-game (open world via AnyKyty)", discussion.Status);
        Assert.Equal(["simnJS/AnyPS5", "custom/AnyPS5-yotei"], discussion.MentionedForks);

        // Tracker tools are filtered out, while searched, mentioned, and catalog forks are combined without duplicates.
        Assert.Equal(
            ["Izamaru/AnyPS5-Yotei", "simnJS/AnyPS5", "custom/AnyPS5-yotei"],
            report.Forks.Select(f => f.Repository));
    }

    private sealed class FakeGitHub(Dictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json)),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
