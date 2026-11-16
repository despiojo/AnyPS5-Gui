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
    public void A_build_that_is_not_a_numbered_version_is_never_taken_for_the_newest()
    {
        using var temp = new TempDirectory();
        foreach (var tag in new[] { "v0.1.5", "vmain-a6606d5-pr19326", "vmain-a6606d5" })
        {
            temp.Write($@"components\{tag}\relinker.exe");
            temp.Write($@"components\{tag}\libs\libc.prx");
        }
        var store = new ComponentStore(temp.Combine("components"));

        Assert.Equal(["v0.1.5", "vmain-a6606d5-pr19326", "vmain-a6606d5"], store.InstalledVersions());
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
