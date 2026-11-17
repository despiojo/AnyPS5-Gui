using System.Runtime.InteropServices;

namespace AnyPS5Gui.Core.Conversion;

public enum Placement { Linked, Copied, Skipped }

public sealed record MirrorResult(int Linked, int Copied, int Skipped)
{
    public int Total => Linked + Copied + Skipped;
}

/// <summary>Reproduces a folder tree elsewhere with hard links where the file system allows, copies where it does not.</summary>
public static class FileLinker
{
    /// <summary>
    /// A hard link takes no extra space and is instant, but needs both sides on the same NTFS volume:
    /// on the first link that fails, the rest of the tree is copied.
    /// <para>
    /// A file already at the destination is left alone when it matches the source in size and time,
    /// which a link or an earlier copy of it does, and replaced when it does not. Files written since
    /// <paramref name="keepNewerThanUtc"/> are never replaced: that is how the modules the relinker has
    /// just converted survive.
    /// </para>
    /// </summary>
    public static MirrorResult Mirror(string source, string destination, bool preferLinks,
        Func<string, bool>? exclude = null, DateTime? keepNewerThanUtc = null, IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            // The default skips hidden and system files, which are part of a dump like any other.
            AttributesToSkip = 0,
        };
        var files = Directory.EnumerateFiles(source, "*", enumeration)
            .Select(file => Path.GetRelativePath(source, file))
            .Where(relative => exclude is null || !exclude(relative))
            .ToList();

        int linked = 0, copied = 0, skipped = 0, done = 0;
        var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var from = Path.Combine(source, relative);
            var to = Path.Combine(destination, relative);

            var directory = Path.GetDirectoryName(to)!;
            if (createdDirectories.Add(directory)) Directory.CreateDirectory(directory);

            switch (PlaceInExistingDirectory(from, to, preferLinks, keepNewerThanUtc))
            {
                case Placement.Linked:
                    linked++;
                    break;
                case Placement.Copied:
                    preferLinks = false;
                    copied++;
                    break;
                default:
                    skipped++;
                    break;
            }

            done++;
            if (done % 64 == 0 || done == files.Count) progress?.Report((done, files.Count));
        }

        return new MirrorResult(linked, copied, skipped);
    }

    /// <summary>
    /// One file of a mirror, by the same rules, creating its folder. A caller placing several stops
    /// asking for links once one comes back copied.
    /// </summary>
    public static Placement Place(string from, string to, bool preferLinks, DateTime? keepNewerThanUtc = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(to))!);
        return PlaceInExistingDirectory(from, to, preferLinks, keepNewerThanUtc);
    }

    private static Placement PlaceInExistingDirectory(string from, string to, bool preferLinks, DateTime? keepNewerThanUtc)
    {
        var existing = new FileInfo(to);
        if (existing.Exists)
        {
            if (IsUpToDate(existing, new FileInfo(from)) || existing.LastWriteTimeUtc >= keepNewerThanUtc)
                return Placement.Skipped;

            // A stale link shares its attributes with a file nothing else here uses any more.
            existing.IsReadOnly = false;
            existing.Delete();
        }

        if (preferLinks && TryCreateHardLink(to, from)) return Placement.Linked;
        File.Copy(from, to);
        return Placement.Copied;
    }

    // Two seconds is the coarsest time stamp a Windows file system keeps (FAT), so a copy on one still matches.
    private static bool IsUpToDate(FileInfo existing, FileInfo original) =>
        existing.Length == original.Length &&
        (existing.LastWriteTimeUtc - original.LastWriteTimeUtc).Duration() <= TimeSpan.FromSeconds(2);

    public static bool TryCreateHardLink(string link, string existingFile) =>
        CreateHardLink(Extended(link), Extended(existingFile), IntPtr.Zero);

    // Game dumps nest deeply; the \\?\ form lifts the 260-character limit for this Win32 call.
    private static string Extended(string path)
    {
        path = Path.GetFullPath(path);
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
