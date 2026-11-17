namespace AnyPS5Gui.Core.Games;

public enum ExecutableKind
{
    Elf,
    /// <summary>A SELF/fSELF container, which the relinker does not read: it is unwrapped first.</summary>
    Self,
    Unknown,
}

/// <summary>
/// Things worth telling the user before converting. Only <see cref="NoExecutable"/> prevents it, and the
/// two about containers are no trouble: the conversion unwraps them.
/// </summary>
public enum GameIssue
{
    NoExecutable,
    WrappedExecutable,
    NotAnElf,
    NoModuleDirectory,
    BothModuleDirectories,
    WrappedModules,
}

/// <summary>What the app found in a dumped game folder.</summary>
public sealed record GameInfo
{
    public required string Folder { get; init; }
    public string? Executable { get; init; }
    public ExecutableKind? Kind { get; init; }
    public ParamJson? Param { get; init; }
    public string? IconPath { get; init; }
    public IReadOnlyList<string> ModuleDirectories { get; init; } = [];
    public int WrappedModules { get; init; }
    public bool HasPatchFile { get; init; }
    public IReadOnlyList<GameIssue> Issues { get; init; } = [];

    public bool CanConvert => Executable is not null;

    /// <summary>The executable or some module is a SELF container, which the relinker cannot read as it is.</summary>
    public bool NeedsUnwrapping => Kind == ExecutableKind.Self || WrappedModules > 0;

    public string DisplayName => Param?.TitleName ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(Folder));
}

public static class GameFolder
{
    public const string PatchFileName = "relinker-patches.toml";

    private static readonly string[] StandardModuleDirectories = ["sce_module", "sce_modules", "prx"];
    private static readonly string[] ExecutableNames = ["eboot.bin", "eboot.elf"];

    /// <summary>Looks at a game folder, or at an executable inside one. Never throws for a missing path.</summary>
    public static GameInfo Inspect(string path)
    {
        path = Path.GetFullPath(path.Trim().Trim('"'));

        string folder;
        string? executable;
        if (File.Exists(path))
        {
            executable = path;
            folder = Path.GetDirectoryName(path)!;
        }
        else
        {
            folder = path;
            executable = Directory.Exists(folder) ? FindExecutable(folder) : null;
        }

        var issues = new List<GameIssue>();
        ExecutableKind? kind = null;
        if (executable is null)
        {
            issues.Add(GameIssue.NoExecutable);
        }
        else
        {
            kind = Identify(executable);
            if (kind == ExecutableKind.Self) issues.Add(GameIssue.WrappedExecutable);
            else if (kind == ExecutableKind.Unknown) issues.Add(GameIssue.NotAnElf);
        }

        var moduleDirectories = Directory.Exists(folder)
            ? StandardModuleDirectories.Where(name => Directory.Exists(Path.Combine(folder, name))).ToList()
            : [];
        var wrappedModules = 0;
        if (executable is not null)
        {
            if (moduleDirectories.Count == 0) issues.Add(GameIssue.NoModuleDirectory);
            if (moduleDirectories.Contains("sce_module") && moduleDirectories.Contains("sce_modules"))
                issues.Add(GameIssue.BothModuleDirectories);

            // Only files directly inside the module directories are relinked.
            wrappedModules = moduleDirectories
                .SelectMany(name => Directory.EnumerateFiles(Path.Combine(folder, name)))
                .Count(file => Identify(file) == ExecutableKind.Self);
            if (wrappedModules > 0) issues.Add(GameIssue.WrappedModules);
        }

        var icon = Path.Combine(folder, "sce_sys", "icon0.png");
        return new GameInfo
        {
            Folder = folder,
            Executable = executable,
            Kind = kind,
            Param = ParamJson.Read(Path.Combine(folder, "sce_sys", "param.json")),
            IconPath = File.Exists(icon) ? icon : null,
            ModuleDirectories = moduleDirectories,
            WrappedModules = wrappedModules,
            HasPatchFile = File.Exists(Path.Combine(folder, PatchFileName)),
            Issues = issues,
        };
    }

    public static ExecutableKind Identify(string file)
    {
        Span<byte> magic = stackalloc byte[4];
        try
        {
            using var stream = File.OpenRead(file);
            if (stream.ReadAtLeast(magic, 4, throwOnEndOfStream: false) < 4) return ExecutableKind.Unknown;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ExecutableKind.Unknown;
        }

        if (magic.SequenceEqual<byte>([0x7F, (byte)'E', (byte)'L', (byte)'F'])) return ExecutableKind.Elf;
        return SelfContainer.IsContainer(magic) ? ExecutableKind.Self : ExecutableKind.Unknown;
    }

    private static string? FindExecutable(string folder)
    {
        foreach (var name in ExecutableNames)
        {
            var candidate = Path.Combine(folder, name);
            if (File.Exists(candidate)) return candidate;
        }

        // A homebrew folder with a single .elf needs no guessing either.
        var elves = Directory.GetFiles(folder, "*.elf");
        return elves.Length == 1 ? elves[0] : null;
    }
}
