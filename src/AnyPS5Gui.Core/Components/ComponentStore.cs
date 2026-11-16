using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AnyPS5Gui.Core.Components;

public enum InstallStage { Relinker, Libraries, Extracting }

public sealed record InstallProgress(InstallStage Stage, long Done, long Total);

/// <summary>The relinker and library folder a conversion uses, wherever they come from.</summary>
public sealed record ResolvedComponents(string? RelinkerPath, string? LibsPath, string? Version)
{
    public bool HasRelinker => RelinkerPath is not null;
}

/// <summary>
/// AnyPS5 releases downloaded by the app, one folder per version:
/// <code>
/// components/
///     v0.1.5/
///         relinker.exe
///         libs/*.prx
/// </code>
/// </summary>
public sealed partial class ComponentStore(string root)
{
    public string Root { get; } = root;

    public string RelinkerPath(string tag) => Path.Combine(VersionDirectory(tag), "relinker.exe");
    public string LibsPath(string tag) => Path.Combine(VersionDirectory(tag), "libs");

    public bool IsInstalled(string tag) =>
        IsValidTag(tag) && File.Exists(RelinkerPath(tag)) && Directory.Exists(LibsPath(tag));

    /// <summary>Newest version first.</summary>
    public IReadOnlyList<string> InstalledVersions()
    {
        if (!Directory.Exists(Root)) return [];
        return Directory.GetDirectories(Root)
            .Select(Path.GetFileName)
            .Where(name => name is not null && IsInstalled(name))
            .Select(name => name!)
            .OrderByDescending(name => name, VersionOrder.Instance)
            .ToList();
    }

    /// <summary>Custom paths from the settings win over a downloaded version; the newest download is the fallback.</summary>
    public ResolvedComponents Resolve(AppSettings settings)
    {
        var version = settings.ActiveVersion is { } active && IsInstalled(active)
            ? active
            : InstalledVersions().FirstOrDefault();

        var relinker = !string.IsNullOrWhiteSpace(settings.CustomRelinkerPath) && File.Exists(settings.CustomRelinkerPath)
            ? settings.CustomRelinkerPath
            : version is not null ? RelinkerPath(version) : null;
        var libs = !string.IsNullOrWhiteSpace(settings.CustomLibsPath) && Directory.Exists(settings.CustomLibsPath)
            ? settings.CustomLibsPath
            : version is not null ? LibsPath(version) : null;
        return new ResolvedComponents(relinker, libs, version);
    }

    /// <summary>
    /// Downloads and unpacks a release. The version folder only appears once everything is in place,
    /// so an interrupted download never looks installed.
    /// </summary>
    public async Task InstallAsync(ReleaseInfo release, HttpClient http, IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidTag(release.Tag)) throw new ArgumentException($"'{release.Tag}' is not a usable version name.");
        if (release.Relinker is null || release.Libraries is null)
            throw new InvalidOperationException($"Release {release.Tag} has no Windows relinker or libraries.");

        var staging = Path.Combine(Root, ".staging-" + release.Tag);
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        try
        {
            await DownloadAsync(http, release.Relinker, Path.Combine(staging, "relinker.exe"),
                InstallStage.Relinker, progress, cancellationToken).ConfigureAwait(false);

            var archive = Path.Combine(staging, "libs.zip");
            await DownloadAsync(http, release.Libraries, archive, InstallStage.Libraries, progress, cancellationToken)
                .ConfigureAwait(false);
            await Task.Run(() => ExtractLibraries(archive, Path.Combine(staging, "libs"), progress, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            File.Delete(archive);

            var final = VersionDirectory(release.Tag);
            if (Directory.Exists(final)) Directory.Delete(final, recursive: true);
            Directory.Move(staging, final);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public void Remove(string tag)
    {
        if (IsValidTag(tag) && Directory.Exists(VersionDirectory(tag))) Directory.Delete(VersionDirectory(tag), recursive: true);
    }

    /// <summary>A tag becomes a folder name, so it may hold nothing a path could abuse.</summary>
    public static bool IsValidTag(string tag) => TagPattern().IsMatch(tag) && !tag.Contains("..");

    /// <summary>
    /// Unpacks the entries under <c>libs/</c> of the release archive into <paramref name="destination"/>.
    /// Entries that would land outside it are refused.
    /// </summary>
    internal static void ExtractLibraries(string archivePath, string destination,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        const string prefix = "libs/";
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(destination);

        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries
            .Where(entry => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && entry.Name.Length > 0)
            .ToList();
        if (entries.Count == 0) throw new InvalidDataException("The archive has no libs/ folder.");

        var done = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName[prefix.Length..]));
            if (!target.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The archive entry '{entry.FullName}' points outside the destination.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            progress?.Report(new InstallProgress(InstallStage.Extracting, ++done, entries.Count));
        }
    }

    private static async Task DownloadAsync(HttpClient http, ReleaseAsset asset, string destination,
        InstallStage stage, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? asset.Size;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var file = File.Create(destination))
        {
            var buffer = new byte[1 << 17];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                done += read;
                progress?.Report(new InstallProgress(stage, done, total));
            }
        }

        if (asset.Sha256 is not null)
        {
            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{asset.Name} does not match the checksum GitHub lists for it.");
        }
    }

    private string VersionDirectory(string tag) => Path.Combine(Root, tag);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex TagPattern();

    /// <summary>
    /// Orders v0.1.10 after v0.1.9, which plain text order gets wrong. A name that is not a version number,
    /// such as a build from another source, comes after every numbered one: it is never taken for the newest.
    /// </summary>
    private sealed class VersionOrder : IComparer<string>
    {
        public static readonly VersionOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            var a = Parse(x);
            var b = Parse(y);
            if (a is not null && b is not null) return a.CompareTo(b);
            if (a is null && b is null) return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            return a is null ? -1 : 1;
        }

        private static Version? Parse(string? tag) =>
            tag is not null && Version.TryParse(tag.TrimStart('v', 'V'), out var version) ? version : null;
    }
}
