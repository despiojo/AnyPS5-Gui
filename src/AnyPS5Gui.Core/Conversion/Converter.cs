using AnyPS5Gui.Core.Games;
using AnyPS5Gui.Core.Relinker;

namespace AnyPS5Gui.Core.Conversion;

public sealed record ConversionRequest
{
    public required GameInfo Game { get; init; }
    public required string OutputDirectory { get; init; }
    /// <summary>File name of the converted executable, with its <c>.exe</c>.</summary>
    public required string ExecutableName { get; init; }
    public required RelinkerOptions Options { get; init; }
    public required string RelinkerPath { get; init; }
    /// <summary>Folder holding the AnyPS5 system libraries. Null leaves <c>libs/</c> to the user.</summary>
    public string? LibsPath { get; init; }
    public bool LinkFiles { get; init; } = true;

    public string OutputExecutable => Path.Combine(OutputDirectory, ExecutableName);
}

public enum ConversionStage { Unwrapping, Relinking, Libraries, GameFiles }

public sealed record ConversionProgress(ConversionStage Stage, int Done = 0, int Total = 0);

public enum ConversionProblem
{
    NoExecutable,
    RelinkerMissing,
    OutputInsideGame,
    GameInsideOutput,
    /// <summary>A SELF container of the game is still encrypted. <see cref="ConversionException.Detail"/> names it.</summary>
    EncryptedContainer,
    CompressedContainer,
    MalformedContainer,
}

/// <summary>A request that cannot be started. The relinker's own failures are reported through the exit code instead.</summary>
public sealed class ConversionException(ConversionProblem problem, string? detail = null) : Exception(problem.ToString())
{
    public ConversionProblem Problem { get; } = problem;

    /// <summary>The file the problem is about, for the problems that are about one.</summary>
    public string? Detail { get; } = detail;
}

public sealed record ConversionResult(int ExitCode, IReadOnlyList<string> Command)
{
    public MirrorResult? Libraries { get; init; }
    public MirrorResult? GameFiles { get; init; }
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Converts a game and lays out what it needs to run:
/// <code>
/// output/
///     GAME.exe      written by the relinker
///     libs/         AnyPS5 system libraries
///     app0/         the game's own files, plus the modules the relinker converted
/// </code>
/// A dump usually comes with its executable and modules as SELF containers, which the relinker does not
/// read. Those are unwrapped first, straight into <c>app0/</c>, where they are what the game is left
/// with instead of the containers, and the relinker is pointed there. The dump is only ever read. A dry
/// run, which may not write to the output, unwraps into a temporary folder that it removes afterwards.
/// </summary>
public static class Converter
{
    public static List<string> BuildArguments(ConversionRequest request)
    {
        var executable = request.Game.Executable ?? throw new ConversionException(ConversionProblem.NoExecutable);
        if (UnwrapDirectory(request) is not { } unwrapped)
            return RelinkerCommand.Build(request.Options, executable, Path.GetFullPath(request.OutputExecutable));

        // The relinker looks for the patch file beside its input, which is no longer where the game is.
        var patches = Path.Combine(request.Game.Folder, GameFolder.PatchFileName);
        return RelinkerCommand.Build(request.Options, Path.Combine(unwrapped, Path.GetFileName(executable)),
            Path.GetFullPath(request.OutputExecutable), request.Game.HasPatchFile ? patches : null);
    }

    /// <summary>
    /// Where the executable and the modules of a game that comes in containers are unwrapped, and read
    /// by the relinker: <c>app0/</c> of the output, or a temporary folder for a dry run. Nothing for a
    /// game that needs no unwrapping.
    /// </summary>
    public static string? UnwrapDirectory(ConversionRequest request)
    {
        if (!request.Game.NeedsUnwrapping) return null;
        return request.Options.DryRun
            ? Path.Combine(Path.GetTempPath(), "AnyPS5Gui-dry-run-" + Path.GetFileNameWithoutExtension(request.ExecutableName))
            : Path.Combine(Path.GetFullPath(request.OutputDirectory), "app0");
    }

    public static async Task<ConversionResult> RunAsync(ConversionRequest request, Action<string> onOutput,
        Action<string> onError, IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = BuildArguments(request);
        if (!File.Exists(request.RelinkerPath)) throw new ConversionException(ConversionProblem.RelinkerMissing);

        var output = Path.GetFullPath(request.OutputDirectory);
        var game = Path.GetFullPath(request.Game.Folder);
        // Either nesting would make the app0 mirror feed on itself.
        if (IsSameOrInside(output, game)) throw new ConversionException(ConversionProblem.OutputInsideGame);
        if (IsSameOrInside(game, output)) throw new ConversionException(ConversionProblem.GameInsideOutput);

        var dryRun = request.Options.DryRun;
        // With a margin for file systems that round time stamps.
        var started = DateTime.UtcNow - TimeSpan.FromSeconds(3);
        if (!dryRun) Directory.CreateDirectory(output);

        int exitCode;
        var unwrapped = UnwrapDirectory(request);
        try
        {
            if (unwrapped is not null)
            {
                progress?.Report(new ConversionProgress(ConversionStage.Unwrapping));
                // What an interrupted dry run left is not this dump.
                if (dryRun) DeleteCopies(unwrapped);
                // A dry run's copy is thrown away, so nothing in it may be a link to the dump.
                await Task.Run(() => Unwrap(request.Game, unwrapped, request.LinkFiles && !dryRun, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new ConversionProgress(ConversionStage.Relinking));
            exitCode = await ProcessRunner.RunAsync(
                new ProcessSpec(request.RelinkerPath, arguments) { WorkingDirectory = game },
                onOutput, onError, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (unwrapped is not null && dryRun) DeleteCopies(unwrapped);
        }

        var result = new ConversionResult(exitCode, arguments);
        if (exitCode != 0 || dryRun) return result;

        return await Task.Run(() =>
        {
            MirrorResult? libraries = null;
            if (request.LibsPath is not null)
            {
                libraries = FileLinker.Mirror(request.LibsPath, Path.Combine(output, "libs"), request.LinkFiles,
                    progress: Stage(progress, ConversionStage.Libraries), cancellationToken: cancellationToken);
            }

            // The relinker ran first: what it has just written under app0/ is not replaced.
            var gameFiles = FileLinker.Mirror(game, Path.Combine(output, "app0"), request.LinkFiles,
                exclude: relative => relative.Equals(GameFolder.PatchFileName, StringComparison.OrdinalIgnoreCase),
                keepNewerThanUtc: started,
                progress: Stage(progress, ConversionStage.GameFiles), cancellationToken: cancellationToken);

            return result with { Libraries = libraries, GameFiles = gameFiles };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lays out the executable and the files directly inside the module folders, which are the ones the
    /// relinker reads, as they are in the game: a container as the ELF it holds, anything else as it is.
    /// </summary>
    private static void Unwrap(GameInfo game, string destination, bool preferLinks, CancellationToken cancellationToken)
    {
        var files = game.ModuleDirectories
            .SelectMany(name => Directory.EnumerateFiles(Path.Combine(game.Folder, name)))
            .Prepend(game.Executable!);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var to = Path.Combine(destination, Path.GetRelativePath(game.Folder, file));
            if (GameFolder.Identify(file) != ExecutableKind.Self)
            {
                if (FileLinker.Place(file, to, preferLinks) == Placement.Copied) preferLinks = false;
                continue;
            }

            try
            {
                SelfContainer.Unwrap(file, to, cancellationToken);
            }
            catch (SelfException e)
            {
                throw new ConversionException(e.Problem switch
                {
                    SelfProblem.Encrypted => ConversionProblem.EncryptedContainer,
                    SelfProblem.Compressed => ConversionProblem.CompressedContainer,
                    _ => ConversionProblem.MalformedContainer,
                }, Path.GetRelativePath(game.Folder, file));
            }
        }
    }

    /// <summary>Removes a folder of copies. A copy keeps the read-only mark of its original, which would keep it there.</summary>
    private static void DeleteCopies(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    public static bool IsSameOrInside(string path, string container)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        container = Path.TrimEndingDirectorySeparator(Path.GetFullPath(container));
        return path.Equals(container, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(container + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static IProgress<(int Done, int Total)>? Stage(IProgress<ConversionProgress>? progress, ConversionStage stage)
    {
        if (progress is null) return null;
        progress.Report(new ConversionProgress(stage));
        return new SynchronousProgress(value => progress.Report(new ConversionProgress(stage, value.Done, value.Total)));
    }

    // Progress<T> would post to a captured context; the caller's IProgress already decides where reports go.
    private sealed class SynchronousProgress(Action<(int Done, int Total)> report) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => report(value);
    }
}
