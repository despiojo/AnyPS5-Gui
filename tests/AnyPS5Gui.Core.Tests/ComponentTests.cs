using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using AnyPS5Gui.Core.Components;

namespace AnyPS5Gui.Core.Tests;

public class ComponentTests
{
    private const string Releases = """
        [
          { "tag_name": "v0.2.0-rc1", "prerelease": true, "draft": false, "assets": [] },
          {
            "tag_name": "v0.1.5", "prerelease": false, "draft": false, "published_at": "2026-11-11T15:01:01Z",
            "assets": [
              { "name": "relinker-v0.1.5", "size": 5, "browser_download_url": "https://example.test/relinker-v0.1.5" },
              { "name": "relinker-v0.1.5.exe", "size": 5896022, "digest": "sha256:ABCDEF",
                "browser_download_url": "https://example.test/relinker-v0.1.5.exe" },
              { "name": "prx-windows-unpatched-v0.1.5.zip", "size": 1, "browser_download_url": "https://example.test/u.zip" },
              { "name": "prx-windows-v0.1.5.zip", "size": 114702577, "digest": null,
                "browser_download_url": "https://example.test/prx-windows-v0.1.5.zip" }
            ]
          },
          { "tag_name": "v0.1.0", "assets": [ { "name": "relinker-v0.1.0.exe", "size": 1, "browser_download_url": "https://example.test/r" } ] },
          { "tag_name": "../evil", "assets": [] }
        ]
        """;

    [Fact]
    public void Releases_are_read_with_only_the_windows_assets()
    {
        var releases = ReleaseClient.Parse(Releases);

        Assert.Equal(["v0.1.5", "v0.1.0"], releases.Select(release => release.Tag));
        var latest = releases[0];
        Assert.True(latest.IsComplete);
        Assert.Equal(new ReleaseAsset("relinker-v0.1.5.exe", "https://example.test/relinker-v0.1.5.exe", 5896022, "ABCDEF"),
            latest.Relinker);
        Assert.Equal(new ReleaseAsset("prx-windows-v0.1.5.zip", "https://example.test/prx-windows-v0.1.5.zip", 114702577, null),
            latest.Libraries);
        Assert.Equal(5896022 + 114702577, latest.DownloadSize);
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 15, 1, 1, TimeSpan.Zero), latest.PublishedAt);
        Assert.False(releases[1].IsComplete);
    }

    [Fact]
    public void Releases_of_another_repository_carry_its_name()
    {
        var official = ReleaseClient.Parse(Releases)[0];
        Assert.True(official.IsOfficial);
        Assert.Equal("v0.1.5", official.Label);

        var other = ReleaseClient.Parse(Releases, "someone/AnyPS5")[0];
        Assert.False(other.IsOfficial);
        Assert.Equal("someone/AnyPS5", other.Repository);
        Assert.Equal("v0.1.5  ·  someone/AnyPS5", other.Label);

        // What each is downloaded as: the label of a version can be told from its name alone.
        Assert.Equal("v0.1.5", official.VersionName);
        Assert.Equal("v0.1.5@someone~AnyPS5", other.VersionName);
        Assert.Equal(official.Label, ComponentStore.VersionLabel(official.VersionName));
        Assert.Equal(other.Label, ComponentStore.VersionLabel(other.VersionName));
    }

    [Theory]
    [InlineData("v0.1.5", true)]
    [InlineData("v0.1.5@some-one~Any.PS5_fork", true)]
    [InlineData("v0.1.5@someone", false)]
    [InlineData("v0.1.5@a~b~c", false)]
    [InlineData("v0.1.5@a/b", false)]
    [InlineData(@"v0.1.5@a~b\c", false)]
    [InlineData("v0.1.5@a~..", false)]
    [InlineData("@a~b", false)]
    [InlineData(".staging-v0.1.5", false)]
    public void Version_names_must_be_safe_folder_names(string version, bool expected)
    {
        Assert.Equal(expected, ComponentStore.IsValidVersion(version));
    }

    [Fact]
    public async Task The_same_tag_from_two_repositories_is_two_versions()
    {
        using var temp = new TempDirectory();
        var archive = Zip(("libs/libc.prx", "c"));
        using var http = new HttpClient(new FakeDownloads(new()
        {
            ["https://example.test/official.exe"] = "official"u8.ToArray(),
            ["https://example.test/fork.exe"] = "fork"u8.ToArray(),
            ["https://example.test/libs.zip"] = archive,
        }));
        var store = new ComponentStore(temp.Combine("components"));
        var official = new ReleaseInfo("v0.1.5", null,
            new ReleaseAsset("relinker-v0.1.5.exe", "https://example.test/official.exe", 8, null),
            new ReleaseAsset("prx-windows-v0.1.5.zip", "https://example.test/libs.zip", archive.Length, null));
        var fork = official with
        {
            Repository = "someone/AnyPS5",
            Relinker = official.Relinker! with { Url = "https://example.test/fork.exe" },
        };

        await store.InstallAsync(official, http, null, TestContext.Current.CancellationToken);
        Assert.False(store.IsInstalled(fork.VersionName));
        await store.InstallAsync(fork, http, null, TestContext.Current.CancellationToken);

        Assert.Equal(["v0.1.5", "v0.1.5@someone~AnyPS5"], store.InstalledVersions());
        Assert.Equal("official", File.ReadAllText(store.RelinkerPath(official.VersionName)));
        Assert.Equal("fork", File.ReadAllText(store.RelinkerPath(fork.VersionName)));
        Assert.Equal("v0.1.5", store.Resolve(new AppSettings()).Version);
        Assert.Equal(fork.VersionName, store.Resolve(new AppSettings { ActiveVersion = fork.VersionName }).Version);

        store.Remove(fork.VersionName);
        Assert.Equal(["v0.1.5"], store.InstalledVersions());
        Assert.Equal("official", File.ReadAllText(store.RelinkerPath(official.VersionName)));
    }

    [Theory]
    [InlineData("boykopovar/AnyPS5", true)]
    [InlineData("some-one/Any.PS5_fork", true)]
    [InlineData("AnyPS5", false)]
    [InlineData("a/b/c", false)]
    [InlineData("a/..", false)]
    [InlineData("a b/c", false)]
    [InlineData("a/b?per_page=1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_release_source_is_an_owner_and_a_name(string? repository, bool expected)
    {
        Assert.Equal(expected, ReleaseClient.IsValidRepository(repository));
    }

    [Fact]
    public void Multiple_release_sources_are_split_and_deduplicated_without_official_repository()
    {
        Assert.Equal(
            ["Izamaru/AnyPS5-Yotei", "simnJS/AnyPS5"],
            ReleaseClient.SplitRepositories("Izamaru/AnyPS5-Yotei, boykopovar/AnyPS5; simnJS/AnyPS5\nIzamaru/AnyPS5-Yotei"));

        var list = ReleaseClient.AddRepository("Izamaru/AnyPS5-Yotei", "simnJS/AnyPS5");
        Assert.Equal("Izamaru/AnyPS5-Yotei, simnJS/AnyPS5", list);
        Assert.Equal(list, ReleaseClient.AddRepository(list, "izamaru/anyps5-yotei"));
    }

    [Fact]
    public void Resolve_can_target_a_specific_installed_version_for_a_game()
    {
        using var temp = new TempDirectory();
        foreach (var version in new[] { "v0.1.5", "vmain-a72186d@Izamaru~AnyPS5-Yotei" })
        {
            temp.Write(Path.Combine("components", version, "relinker.exe"));
            temp.Write(Path.Combine("components", version, "libs", "libc.prx"));
        }
        var store = new ComponentStore(temp.Combine("components"));
        var settings = new AppSettings { ActiveVersion = "v0.1.5" };

        Assert.Equal("vmain-a72186d@Izamaru~AnyPS5-Yotei",
            store.Resolve(settings, "vmain-a72186d@Izamaru~AnyPS5-Yotei").Version);
        Assert.Equal("Izamaru/AnyPS5-Yotei",
            ComponentStore.RepositoryOf("vmain-a72186d@Izamaru~AnyPS5-Yotei"));
        Assert.Equal(ReleaseClient.Repository, ComponentStore.RepositoryOf("v0.1.5"));
    }

    [Fact]
    public async Task A_release_list_that_never_arrives_is_a_failed_request_and_not_a_cancellation()
    {
        using var http = new HttpClient(new NoAnswer());
        var client = new ReleaseClient(http) { ResponseTimeout = TimeSpan.FromMilliseconds(50) };

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetReleasesAsync("someone/AnyPS5", TestContext.Current.CancellationToken));

        // The caller giving up stays what it is.
        using var givenUp = new CancellationTokenSource();
        givenUp.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetReleasesAsync(cancellationToken: givenUp.Token));
    }

    [Fact]
    public async Task The_latest_release_of_a_repository_is_the_one_GitHub_marks_as_such()
    {
        const string latest = """
            {
              "tag_name": "vmain-a72186d", "prerelease": false, "draft": false, "published_at": "2026-11-17T06:07:03Z",
              "assets": [
                { "name": "relinker-vmain-a72186d.exe", "size": 7, "browser_download_url": "https://example.test/r.exe" },
                { "name": "prx-windows-vmain-a72186d.zip", "size": 9, "browser_download_url": "https://example.test/p.zip" }
              ]
            }
            """;
        using var http = new HttpClient(new FakeDownloads(new()
        {
            ["https://api.github.com/repos/someone/AnyPS5/releases/latest"] = System.Text.Encoding.UTF8.GetBytes(latest),
        }));
        var client = new ReleaseClient(http);

        var release = await client.GetLatestReleaseAsync("someone/AnyPS5", TestContext.Current.CancellationToken);

        Assert.NotNull(release);
        Assert.Equal("vmain-a72186d@someone~AnyPS5", release.VersionName);
        Assert.True(release.IsComplete);
        Assert.Equal(16, release.DownloadSize);

        // GitHub answers 404 for a repository that has published nothing: that is not a failure.
        Assert.Null(await client.GetLatestReleaseAsync("nobody/Nothing", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetLatestReleaseAsync("a/b?per_page=1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_fork_with_no_release_of_its_own_uses_a_build_from_our_pipeline()
    {
        const string pipelineReleases = """
            [
              {
                "tag_name": "vmain-4f41f4c", "name": "vmain-4f41f4c (Izamaru/AnyPS5-Yotei)",
                "prerelease": false, "draft": false,
                "body": "Unofficial Windows build of [Izamaru/AnyPS5-Yotei](https://github.com/Izamaru/AnyPS5-Yotei).\n- Base: `main` at Izamaru/AnyPS5-Yotei@4f41f4c375f5abb487dbd184fbc150f31994d762",
                "assets": [
                  { "name": "relinker-vmain-4f41f4c.exe", "size": 10, "browser_download_url": "https://example.test/r.exe" },
                  { "name": "prx-windows-vmain-4f41f4c.zip", "size": 20, "browser_download_url": "https://example.test/p.zip" }
                ]
              },
              {
                "tag_name": "vmain-a72186d", "name": "vmain-a72186d",
                "prerelease": false, "draft": false,
                "body": "- Base: `main` at boykopovar/AnyPS5@a72186d000000000000000000000000000000000",
                "assets": [
                  { "name": "relinker-vmain-a72186d.exe", "size": 10, "browser_download_url": "https://example.test/r2.exe" },
                  { "name": "prx-windows-vmain-a72186d.zip", "size": 20, "browser_download_url": "https://example.test/p2.zip" }
                ]
              }
            ]
            """;
        using var http = new HttpClient(new FakeDownloads(new()
        {
            ["https://api.github.com/repos/Izamaru/AnyPS5-Yotei/releases?per_page=15"] = "[]"u8.ToArray(),
            [$"https://api.github.com/repos/{ReleaseClient.PipelineRepository}/releases?per_page=30"] =
                System.Text.Encoding.UTF8.GetBytes(pipelineReleases),
        }));
        var client = new ReleaseClient(http);

        var releases = await client.GetReleasesAsync("Izamaru/AnyPS5-Yotei", TestContext.Current.CancellationToken);
        var single = Assert.Single(releases);
        Assert.Equal("vmain-4f41f4c", single.Tag);
        Assert.Equal("Izamaru/AnyPS5-Yotei", single.Repository);
        Assert.Equal("vmain-4f41f4c@Izamaru~AnyPS5-Yotei", single.VersionName);
        Assert.True(single.IsComplete);

        var latest = await client.GetLatestReleaseAsync("Izamaru/AnyPS5-Yotei", TestContext.Current.CancellationToken);
        Assert.NotNull(latest);
        Assert.Equal("vmain-4f41f4c@Izamaru~AnyPS5-Yotei", latest.VersionName);
    }

    [Fact]
    public async Task A_fork_without_any_build_launches_one_on_our_pipeline_and_waits_for_it()
    {
        const string repoMeta = """{ "default_branch": "AnyKyty" }""";
        const string commitMeta = """{ "sha": "99cc4a54c83bf152b7979d1d762d51cc860c33e7" }""";
        const string publishedReleases = """
            [
              {
                "tag_name": "vAnyKyty-99cc4a5", "name": "vAnyKyty-99cc4a5 (simnJS/AnyPS5)",
                "prerelease": false, "draft": false,
                "body": "- Base: `AnyKyty` at simnJS/AnyPS5@99cc4a54c83bf152b7979d1d762d51cc860c33e7",
                "assets": [
                  { "name": "relinker-vAnyKyty-99cc4a5.exe", "size": 10, "browser_download_url": "https://example.test/r.exe" },
                  { "name": "prx-windows-vAnyKyty-99cc4a5.zip", "size": 20, "browser_download_url": "https://example.test/p.zip" }
                ]
              }
            ]
            """;

        var files = new Dictionary<string, byte[]>
        {
            ["https://api.github.com/repos/simnJS/AnyPS5"] = System.Text.Encoding.UTF8.GetBytes(repoMeta),
            ["https://api.github.com/repos/simnJS/AnyPS5/commits/AnyKyty"] = System.Text.Encoding.UTF8.GetBytes(commitMeta),
            [$"https://api.github.com/repos/{ReleaseClient.PipelineRepository}/releases?per_page=30"] = "[]"u8.ToArray(),
            [$"https://api.github.com/repos/{ReleaseClient.PipelineRepository}/actions/workflows/{ReleaseClient.PipelineWorkflow}/runs?per_page=20"] =
                """{ "workflow_runs": [] }"""u8.ToArray(),
        };

        PipelineBuildTarget? dispatched = null;
        using var http = new HttpClient(new FakeDownloads(files));
        var client = new ReleaseClient(http)
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            PipelineDispatcher = (target, _) =>
            {
                dispatched = target;
                files[$"https://api.github.com/repos/{ReleaseClient.PipelineRepository}/releases?per_page=30"] =
                    System.Text.Encoding.UTF8.GetBytes(publishedReleases);
                return Task.CompletedTask;
            },
        };

        var release = await client.EnsurePipelineReleaseAsync("simnJS/AnyPS5", null, TestContext.Current.CancellationToken);

        Assert.NotNull(dispatched);
        Assert.Equal(new PipelineBuildTarget("simnJS/AnyPS5", "AnyKyty", "99cc4a54c83bf152b7979d1d762d51cc860c33e7", "vAnyKyty-99cc4a5"),
            dispatched);
        Assert.NotNull(release);
        Assert.Equal("vAnyKyty-99cc4a5@simnJS~AnyPS5", release.VersionName);
    }

    [Fact]
    public async Task A_latest_release_that_never_arrives_is_a_failed_request()
    {
        using var http = new HttpClient(new NoAnswer());
        var client = new ReleaseClient(http) { ResponseTimeout = TimeSpan.FromMilliseconds(50) };

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetLatestReleaseAsync("someone/AnyPS5", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_release_list_that_GitHub_refuses_is_a_failed_request()
    {
        using var http = new HttpClient(new FakeDownloads([]));
        var client = new ReleaseClient(http);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetReleasesAsync("nobody/Nothing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_build_that_is_not_a_numbered_version_is_never_taken_for_the_newest()
    {
        using var temp = new TempDirectory();
        foreach (var version in new[] { "v0.1.5", "vmain-a6606d5-pr19326", "vmain-a6606d5", "v9.9.9@someone~AnyPS5" })
        {
            temp.Write($@"components\{version}\relinker.exe");
            temp.Write($@"components\{version}\libs\libc.prx");
        }
        var store = new ComponentStore(temp.Combine("components"));

        Assert.Equal(["v0.1.5", "vmain-a6606d5-pr19326", "vmain-a6606d5", "v9.9.9@someone~AnyPS5"], store.InstalledVersions());
        Assert.Equal("v0.1.5", store.Resolve(new AppSettings()).Version);
        Assert.Equal("vmain-a6606d5", store.Resolve(new AppSettings { ActiveVersion = "vmain-a6606d5" }).Version);
    }

    [Theory]
    [InlineData("v0.1.5", true)]
    [InlineData("0.1.5-beta_2", true)]
    [InlineData("", false)]
    [InlineData("..", false)]
    [InlineData("v1/../x", false)]
    [InlineData("v1..2", false)]
    [InlineData(".hidden", false)]
    public void Tags_must_be_safe_folder_names(string tag, bool expected)
    {
        Assert.Equal(expected, ComponentStore.IsValidTag(tag));
    }

    [Fact]
    public void Installed_versions_are_listed_newest_first_and_resolved()
    {
        using var temp = new TempDirectory();
        foreach (var tag in new[] { "v0.1.9", "v0.1.10", "v0.1.2" })
        {
            temp.Write($@"components\{tag}\relinker.exe");
            temp.Write($@"components\{tag}\libs\libc.prx");
        }
        temp.Write(@"components\v0.3.0\relinker.exe");
        temp.Write(@"custom\relinker.exe");
        var store = new ComponentStore(temp.Combine("components"));

        Assert.Equal(["v0.1.10", "v0.1.9", "v0.1.2"], store.InstalledVersions());
        Assert.False(store.IsInstalled("v0.3.0"));

        Assert.Equal(new ResolvedComponents(store.RelinkerPath("v0.1.10"), store.LibsPath("v0.1.10"), "v0.1.10"),
            store.Resolve(new AppSettings()));
        Assert.Equal("v0.1.2", store.Resolve(new AppSettings { ActiveVersion = "v0.1.2" }).Version);
        Assert.Equal("v0.1.10", store.Resolve(new AppSettings { ActiveVersion = "v9.9.9" }).Version);

        var custom = store.Resolve(new AppSettings
        {
            CustomRelinkerPath = temp.Combine("custom", "relinker.exe"),
            CustomLibsPath = temp.Combine("missing"),
        });
        Assert.Equal(temp.Combine("custom", "relinker.exe"), custom.RelinkerPath);
        Assert.Equal(store.LibsPath("v0.1.10"), custom.LibsPath);

        store.Remove("v0.1.10");
        Assert.Equal(["v0.1.9", "v0.1.2"], store.InstalledVersions());
        Assert.False(new ComponentStore(temp.Combine("nothing")).Resolve(new AppSettings()).HasRelinker);
    }

    [Fact]
    public void Fork_numbered_versions_are_ordered_numerically_for_fallback()
    {
        using var temp = new TempDirectory();
        foreach (var version in new[]
        {
            "v0.1.9@someone~AnyPS5", "v0.1.10@someone~AnyPS5", "v0.1.2@another~AnyPS5",
            "vmain@someone~AnyPS5",
        })
        {
            temp.Write(Path.Combine("components", version, "relinker.exe"));
            temp.Write(Path.Combine("components", version, "libs", "libc.prx"));
        }
        var store = new ComponentStore(temp.Combine("components"));

        Assert.Equal(
            ["v0.1.10@someone~AnyPS5", "v0.1.9@someone~AnyPS5", "v0.1.2@another~AnyPS5", "vmain@someone~AnyPS5"],
            store.InstalledVersions());
        Assert.Equal("v0.1.10@someone~AnyPS5", store.Resolve(new AppSettings { ActiveVersion = "missing" }).Version);
        Assert.Equal("v0.1.9@someone~AnyPS5",
            store.Resolve(new AppSettings { ActiveVersion = "v0.1.9@someone~AnyPS5" }).Version);

        temp.Write(Path.Combine("components", "v0.1.1", "relinker.exe"));
        temp.Write(Path.Combine("components", "v0.1.1", "libs", "libc.prx"));
        Assert.Equal("v0.1.1", store.Resolve(new AppSettings { ActiveVersion = "missing" }).Version);
    }

    [Fact]
    public void Extraction_takes_the_libs_folder_and_refuses_paths_that_escape_it()
    {
        using var temp = new TempDirectory();
        var good = temp.Combine("good.zip");
        File.WriteAllBytes(good, Zip(("libs/libc.prx", "c"), ("libs/licenses/mimalloc.txt", "m"), ("other/readme.txt", "r"), ("libs/", "")));
        var evil = temp.Combine("evil.zip");
        File.WriteAllBytes(evil, Zip(("libs/ok.prx", "o"), ("libs/../../evil.prx", "e")));
        var empty = temp.Combine("empty.zip");
        File.WriteAllBytes(empty, Zip(("readme.txt", "r")));

        ComponentStore.ExtractLibraries(good, temp.Combine("good"), null, CancellationToken.None);

        Assert.Equal("c", File.ReadAllText(temp.Combine("good", "libc.prx")));
        Assert.Equal("m", File.ReadAllText(temp.Combine("good", "licenses", "mimalloc.txt")));
        Assert.False(File.Exists(temp.Combine("good", "readme.txt")));
        Assert.Throws<InvalidDataException>(() => ComponentStore.ExtractLibraries(evil, temp.Combine("evil", "libs"), null, CancellationToken.None));
        Assert.False(File.Exists(temp.Combine("evil.prx")));
        Assert.False(File.Exists(temp.Combine("evil", "evil.prx")));
        Assert.Throws<InvalidDataException>(() => ComponentStore.ExtractLibraries(empty, temp.Combine("empty"), null, CancellationToken.None));
    }

    [Fact]
    public async Task Install_downloads_verifies_and_unpacks()
    {
        using var temp = new TempDirectory();
        var relinker = "MZ fake relinker"u8.ToArray();
        var archive = Zip(("libs/libc.prx", "c"));
        using var http = new HttpClient(new FakeDownloads(new()
        {
            ["https://example.test/relinker.exe"] = relinker,
            ["https://example.test/libs.zip"] = archive,
        }));
        var store = new ComponentStore(temp.Combine("components"));
        var release = new ReleaseInfo("v0.1.5", null,
            new ReleaseAsset("relinker-v0.1.5.exe", "https://example.test/relinker.exe", relinker.Length, Sha256(relinker)),
            new ReleaseAsset("prx-windows-v0.1.5.zip", "https://example.test/libs.zip", archive.Length, null));
        var stages = new List<InstallStage>();

        await store.InstallAsync(release, http, new Collector(stages), TestContext.Current.CancellationToken);

        Assert.True(store.IsInstalled("v0.1.5"));
        Assert.Equal(relinker, File.ReadAllBytes(store.RelinkerPath("v0.1.5")));
        Assert.Equal("c", File.ReadAllText(Path.Combine(store.LibsPath("v0.1.5"), "libc.prx")));
        Assert.Equal(["v0.1.5"], Directory.GetFileSystemEntries(store.Root).Select(Path.GetFileName));
        Assert.Equal([InstallStage.Relinker, InstallStage.Libraries, InstallStage.Extracting], stages.Distinct());
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_checksum_is_not_installed()
    {
        using var temp = new TempDirectory();
        using var http = new HttpClient(new FakeDownloads(new()
        {
            ["https://example.test/relinker.exe"] = "tampered"u8.ToArray(),
            ["https://example.test/libs.zip"] = Zip(("libs/libc.prx", "c")),
        }));
        var store = new ComponentStore(temp.Combine("components"));
        var release = new ReleaseInfo("v0.1.5", null,
            new ReleaseAsset("relinker-v0.1.5.exe", "https://example.test/relinker.exe", 8, Sha256("original"u8.ToArray())),
            new ReleaseAsset("prx-windows-v0.1.5.zip", "https://example.test/libs.zip", 1, null));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.InstallAsync(release, http, null, TestContext.Current.CancellationToken));

        Assert.False(store.IsInstalled("v0.1.5"));
        Assert.Empty(Directory.GetFileSystemEntries(store.Root));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InstallAsync(
            release with { Libraries = null }, http, null, TestContext.Current.CancellationToken));
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }
        return buffer.ToArray();
    }

    private sealed class Collector(List<InstallStage> stages) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) { lock (stages) stages.Add(value.Stage); }
    }

    private sealed class NoAnswer : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FakeDownloads(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(files.TryGetValue(request.RequestUri!.ToString(), out var content)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
