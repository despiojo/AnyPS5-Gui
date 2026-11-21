using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnyPS5Gui.Core.Components;

public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

/// <summary>The default branch, head commit and release tag our pipeline builds for a fork repository.</summary>
public sealed record PipelineBuildTarget(string Repository, string Branch, string CommitSha, string Tag);

/// <summary>A workflow run on our pipeline repository.</summary>
public sealed record PipelineRunStatus(long Id, string Status, string? Conclusion, string HtmlUrl)
{
    public bool IsCompleted => Status.Equals("completed", StringComparison.OrdinalIgnoreCase);
    public bool IsSuccess => IsCompleted && string.Equals(Conclusion, "success", StringComparison.OrdinalIgnoreCase);
}

/// <summary>An AnyPS5 release and the two files of it the app needs on Windows.</summary>
public sealed record ReleaseInfo(string Tag, DateTimeOffset? PublishedAt, ReleaseAsset? Relinker, ReleaseAsset? Libraries)
{
    /// <summary>The GitHub repository the release is from: AnyPS5's own, unless the user added another source.</summary>
    public string Repository { get; init; } = ReleaseClient.Repository;

    public bool IsOfficial => Repository.Equals(ReleaseClient.Repository, StringComparison.OrdinalIgnoreCase);

    /// <summary>The name the release is downloaded under and chosen by afterwards.</summary>
    public string VersionName => ComponentStore.VersionName(Tag, Repository);

    /// <summary>What the list of versions shows: a build from elsewhere carries the name of where it is from.</summary>
    public string Label => ComponentStore.VersionLabel(VersionName);

    public bool IsComplete => Relinker is not null && Libraries is not null;
    public long DownloadSize => (Relinker?.Size ?? 0) + (Libraries?.Size ?? 0);
}

/// <summary>Reads the release list of a GitHub repository, AnyPS5's own by default, from the public GitHub API.</summary>
public sealed partial class ReleaseClient(HttpClient http)
{
    public const string Repository = "boykopovar/AnyPS5";
    public const string PipelineRepository = "despiojo/AnyPS5-Gui";
    public const string LegacyPipelineRepository = "despiojo/AnyPS5";
    public const string PipelineWorkflow = "patched-build.yml";
    public const string PipelineBranch = "main";

    public static bool IsPipelineRepository(string repository) =>
        repository.Equals(PipelineRepository, StringComparison.OrdinalIgnoreCase) ||
        repository.Equals(LegacyPipelineRepository, StringComparison.OrdinalIgnoreCase);

    /// <summary>An <c>owner/name</c> that can go into a GitHub URL as it is.</summary>
    public static bool IsValidRepository(string? repository) =>
        repository is not null && RepositoryPattern().IsMatch(repository) && !repository.Contains("..");

    /// <summary>
    /// Splits a setting string into individual repository names (separated by commas, semicolons, lines
    /// or spaces), leaving out AnyPS5's own repository and duplicates.
    /// </summary>
    public static IReadOnlyList<string> SplitRepositories(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Repository };
        foreach (var part in text.Split([',', ';', '\r', '\n', ' ', '\t'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var clean = part.Trim('"');
            if (clean.Length > 0 && seen.Add(clean)) result.Add(clean);
        }
        return result;
    }

    /// <summary>Adds <paramref name="repository"/> to a setting string when it is not already listed.</summary>
    public static string AddRepository(string? existing, string repository)
    {
        var list = SplitRepositories(existing).ToList();
        if (!repository.Equals(Repository, StringComparison.OrdinalIgnoreCase) &&
            !list.Any(item => item.Equals(repository, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(repository);
        }
        return string.Join(", ", list);
    }

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

    /// <summary>How long to wait between checks while our pipeline builds a fork release.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Optional override for dispatching a pipeline workflow run (used by tests).</summary>
    public Func<PipelineBuildTarget, CancellationToken, Task>? PipelineDispatcher { get; init; }

    /// <summary>
    /// Newest first, drafts and prereleases left out. When a fork publishes no Windows release of its
    /// own, builds of that fork published on our pipeline (<see cref="PipelineRepository"/>) are used.
    /// An answer that does not arrive in time is a failed request, an <see cref="HttpRequestException"/>:
    /// only the caller's own token cancels.
    /// </summary>
    public async Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(string repository = Repository,
        CancellationToken cancellationToken = default)
    {
        var json = await ReadAsync($"https://api.github.com/repos/{Checked(repository)}/releases?per_page=15",
            cancellationToken).ConfigureAwait(false);
        var releases = Parse(json!, repository);
        if (releases.Any(r => r.IsComplete) ||
            repository.Equals(Repository, StringComparison.OrdinalIgnoreCase) ||
            IsPipelineRepository(repository))
        {
            return releases;
        }

        var pipeline = await GetPipelineReleasesAsync(repository, cancellationToken: cancellationToken).ConfigureAwait(false);
        return pipeline.Count > 0 ? pipeline : releases;
    }

    /// <summary>
    /// The release the repository marks as its latest, which is its owner's choice and not always the
    /// first of its list. Falls back to the newest build on our pipeline when the repository publishes
    /// none of its own. Fails as <see cref="GetReleasesAsync"/> does.
    /// </summary>
    public async Task<ReleaseInfo?> GetLatestReleaseAsync(string repository, CancellationToken cancellationToken = default)
    {
        var json = await ReadAsync($"https://api.github.com/repos/{Checked(repository)}/releases/latest",
            cancellationToken, missingIsNothing: true).ConfigureAwait(false);
        if (json is not null)
        {
            using var document = JsonDocument.Parse(json);
            if (ReadRelease(document.RootElement, repository) is { IsComplete: true } direct)
                return direct;
        }

        if (!repository.Equals(Repository, StringComparison.OrdinalIgnoreCase) &&
            !IsPipelineRepository(repository))
        {
            var pipeline = await GetPipelineReleasesAsync(repository, cancellationToken: cancellationToken).ConfigureAwait(false);
            return pipeline.FirstOrDefault(r => r.IsComplete);
        }

        return null;
    }

    /// <summary>
    /// Reads the releases on <see cref="PipelineRepository"/> (and <see cref="LegacyPipelineRepository"/>)
    /// that were built from <paramref name="repository"/> (or from <paramref name="commitSha"/>).
    /// </summary>
    public async Task<IReadOnlyList<ReleaseInfo>> GetPipelineReleasesAsync(string repository, string? commitSha = null,
        CancellationToken cancellationToken = default)
    {
        repository = Checked(repository);
        foreach (var host in new[] { PipelineRepository, LegacyPipelineRepository })
        {
            var json = await ReadAsync($"https://api.github.com/repos/{host}/releases?per_page=30",
                cancellationToken, missingIsNothing: true).ConfigureAwait(false);
            if (json is null) continue;
            var matched = ParsePipelineReleases(json, repository, commitSha);
            if (matched.Count > 0) return matched;
        }
        return [];
    }

    /// <summary>
    /// Resolves the default branch, head commit SHA and release tag that our pipeline builds for
    /// <paramref name="repository"/>.
    /// </summary>
    public async Task<PipelineBuildTarget> GetBuildTargetAsync(string repository, CancellationToken cancellationToken = default)
    {
        repository = Checked(repository);
        var repoJson = await ReadAsync($"https://api.github.com/repos/{repository}", cancellationToken).ConfigureAwait(false);
        using var repoDoc = JsonDocument.Parse(repoJson!);
        var branch = Text(repoDoc.RootElement, "default_branch");
        if (string.IsNullOrWhiteSpace(branch)) branch = "main";

        var commitJson = await ReadAsync(
            $"https://api.github.com/repos/{repository}/commits/{Uri.EscapeDataString(branch)}",
            cancellationToken).ConfigureAwait(false);
        using var commitDoc = JsonDocument.Parse(commitJson!);
        var sha = Text(commitDoc.RootElement, "sha");
        if (string.IsNullOrWhiteSpace(sha) || sha.Length < 7)
            throw new HttpRequestException($"Could not read the latest commit of {repository} ({branch}).");

        return new PipelineBuildTarget(repository, branch, sha, BuildTag(branch, sha));
    }

    public static string BuildTag(string branch, string commitSha)
    {
        var cleanBranch = InvalidTagChars().Replace(branch.Trim(), "-").Trim('-', '.');
        if (cleanBranch.Length == 0) cleanBranch = "main";
        var shortSha = commitSha.Trim()[..Math.Min(7, commitSha.Trim().Length)].ToLowerInvariant();
        return $"v{cleanBranch}-{shortSha}";
    }

    /// <summary>
    /// Finds a workflow run on <see cref="PipelineRepository"/> for <paramref name="target"/>.
    /// </summary>
    public async Task<PipelineRunStatus?> FindPipelineRunAsync(PipelineBuildTarget target, bool includeCompleted = false,
        CancellationToken cancellationToken = default)
    {
        var json = await ReadAsync(
            $"https://api.github.com/repos/{PipelineRepository}/actions/workflows/{PipelineWorkflow}/runs?per_page=20",
            cancellationToken, missingIsNothing: true).ConfigureAwait(false);
        return json is null ? null : ParsePipelineRun(json, target, includeCompleted);
    }

    /// <summary>
    /// Ensures <paramref name="repository"/> has a Windows build by checking our pipeline repository first,
    /// launching a pipeline build of its default branch when none exists yet, and waiting for the release
    /// to be published.
    /// </summary>
    public async Task<ReleaseInfo?> EnsurePipelineReleaseAsync(
        string repository,
        IProgress<(PipelineBuildTarget Target, bool Waiting)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var target = await GetBuildTargetAsync(repository, cancellationToken).ConfigureAwait(false);

        var existing = await GetPipelineReleasesAsync(target.Repository, target.CommitSha, cancellationToken).ConfigureAwait(false);
        if (existing.FirstOrDefault(r => r.IsComplete) is { } ready) return ready;

        var active = await FindPipelineRunAsync(target, includeCompleted: false, cancellationToken).ConfigureAwait(false);
        if (active is null)
        {
            progress?.Report((target, false));
            await TriggerPipelineBuildAsync(target, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report((target, true));
        while (true)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);

            var built = await GetPipelineReleasesAsync(target.Repository, target.CommitSha, cancellationToken).ConfigureAwait(false);
            if (built.FirstOrDefault(r => r.IsComplete) is { } release) return release;

            var run = await FindPipelineRunAsync(target, includeCompleted: true, cancellationToken).ConfigureAwait(false);
            if (run is { IsCompleted: true, IsSuccess: false })
            {
                built = await GetPipelineReleasesAsync(target.Repository, target.CommitSha, cancellationToken).ConfigureAwait(false);
                if (built.FirstOrDefault(r => r.IsComplete) is { } lateRelease) return lateRelease;
                throw new InvalidOperationException(run.Conclusion ?? "failed");
            }
        }
    }

    /// <summary>
    /// Launches <see cref="PipelineWorkflow"/> on <see cref="PipelineRepository"/> for <paramref name="target"/>.
    /// Uses an environment token when present, or the <c>gh</c> CLI authenticated on the machine.
    /// </summary>
    public async Task TriggerPipelineBuildAsync(PipelineBuildTarget target, CancellationToken cancellationToken = default)
    {
        if (PipelineDispatcher is not null)
        {
            await PipelineDispatcher(target, cancellationToken).ConfigureAwait(false);
            return;
        }

        var token = Environment.GetEnvironmentVariable("GH_TOKEN") ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            await DispatchViaApiAsync(target, token.Trim(), cancellationToken).ConfigureAwait(false);
            return;
        }

        var errors = new List<string>();
        var spec = new ProcessSpec("gh",
        [
            "workflow", "run", PipelineWorkflow,
            "--repo", PipelineRepository,
            "--ref", PipelineBranch,
            "-f", $"tag={target.Tag}",
            "-f", $"repo={target.Repository}",
            "-f", $"base={target.Branch}",
        ]);
        int exitCode;
        try
        {
            exitCode = await ProcessRunner.RunAsync(spec, _ => { }, line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) lock (errors) errors.Add(line.Trim());
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new HttpRequestException(
                $"GitHub CLI (gh) is required to launch a build of {target.Repository} on {PipelineRepository}.", e);
        }

        if (exitCode != 0)
        {
            var detail = errors.Count > 0 ? string.Join(" ", errors) : $"gh exited with code {exitCode}.";
            throw new HttpRequestException(
                $"Could not launch the pipeline build of {target.Repository} on {PipelineRepository}: {detail}");
        }
    }

    public static IReadOnlyList<ReleaseInfo> Parse(string json, string repository = Repository)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<ReleaseInfo>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (ReadRelease(release, repository) is { } read) releases.Add(read);
        }
        return releases;
    }

    public static IReadOnlyList<ReleaseInfo> ParsePipelineReleases(string json, string repository, string? commitSha = null)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
        var releases = new List<ReleaseInfo>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (MatchesPipelineRelease(release, repository, commitSha) &&
                ReadRelease(release, repository) is { } read)
            {
                releases.Add(read);
            }
        }
        return releases;
    }

    public static PipelineRunStatus? ParsePipelineRun(string json, PipelineBuildTarget target, bool includeCompleted = false)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("workflow_runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var run in runs.EnumerateArray())
        {
            if (run.ValueKind != JsonValueKind.Object) continue;
            var status = Text(run, "status") ?? "";
            var completed = status.Equals("completed", StringComparison.OrdinalIgnoreCase);
            if (completed && !includeCompleted) continue;

            var title = Text(run, "display_title") ?? Text(run, "name") ?? "";
            if (!title.Contains(target.Tag, StringComparison.OrdinalIgnoreCase) &&
                !title.Contains(target.Repository, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var id = run.TryGetProperty("id", out var idProp) && idProp.TryGetInt64(out var parsedId) ? parsedId : 0;
            var conclusion = Text(run, "conclusion");
            var url = Text(run, "html_url") ?? $"https://github.com/{PipelineRepository}/actions";
            return new PipelineRunStatus(id, status, conclusion, url);
        }
        return null;
    }

    private static bool MatchesPipelineRelease(JsonElement release, string repository, string? commitSha)
    {
        if (release.ValueKind != JsonValueKind.Object) return false;
        var body = Text(release, "body") ?? "";
        var title = Text(release, "name") ?? "";
        if (body.Contains($"at {repository}@", StringComparison.OrdinalIgnoreCase) ||
            body.Contains($"[{repository}]", StringComparison.OrdinalIgnoreCase) ||
            title.Contains($"({repository})", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(commitSha) && commitSha.Length >= 7)
        {
            var shortSha = commitSha[..7];
            if (body.Contains($"@{commitSha}", StringComparison.OrdinalIgnoreCase) &&
                !body.Contains("- Merged:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (Text(release, "tag_name") is { } tag &&
                tag.EndsWith("-" + shortSha, StringComparison.OrdinalIgnoreCase) &&
                !body.Contains("- Merged:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task DispatchViaApiAsync(PipelineBuildTarget target, string token, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ResponseTimeout);
        var url = $"https://api.github.com/repos/{PipelineRepository}/actions/workflows/{PipelineWorkflow}/dispatches";
        var payload = JsonSerializer.Serialize(new
        {
            @ref = PipelineBranch,
            inputs = new
            {
                tag = target.Tag,
                repo = target.Repository,
                @base = target.Branch,
            },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static string Checked(string repository) =>
        IsValidRepository(repository)
            ? repository
            : throw new ArgumentException($"'{repository}' is not a GitHub repository name.", nameof(repository));

    private async Task<string?> ReadAsync(string url, CancellationToken cancellationToken, bool missingIsNothing = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ResponseTimeout);
        try
        {
            using var response = await http.GetAsync(url, timeout.Token).ConfigureAwait(false);
            if (missingIsNothing && response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException($"GitHub did not answer within {ResponseTimeout.TotalSeconds:0.#} seconds.", e);
        }
    }

    /// <summary>Nothing for a draft, a prerelease or a tag that could not name a folder.</summary>
    private static ReleaseInfo? ReadRelease(JsonElement release, string repository)
    {
        if (release.ValueKind != JsonValueKind.Object) return null;
        if (Flag(release, "draft") || Flag(release, "prerelease")) return null;
        if (Text(release, "tag_name") is not { } tag || !ComponentStore.IsValidTag(tag)) return null;

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
        return new ReleaseInfo(tag, published, relinker, libraries) { Repository = repository };
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

    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex InvalidTagChars();
}
