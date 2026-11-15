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

public enum ConversionStage { Relinking, Libraries, GameFiles }

public sealed record ConversionProgress(ConversionStage Stage, int Done = 0, int Total = 0);

public enum ConversionProblem
{
    NoExecutable,
    RelinkerMissing,
    OutputInsideGame,
    GameInsideOutput,
}

/// <summary>A request that cannot be started. The relinker's own failures are reported through the exit code instead.</summary>
public sealed class ConversionException(ConversionProblem problem) : Exception(problem.ToString())
{
    public ConversionProblem Problem { get; } = problem;
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
/// </summary>
public static class Converter
{
    public static List<string> BuildArguments(ConversionRequest request) =>
        RelinkerCommand.Build(request.Options,
            request.Game.Executable ?? throw new ConversionException(ConversionProblem.NoExecutable),
            Path.GetFullPath(request.OutputExecutable));

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

        progress?.Report(new ConversionProgress(ConversionStage.Relinking));
        var exitCode = await ProcessRunner.RunAsync(
            new ProcessSpec(request.RelinkerPath, arguments) { WorkingDirectory = game },
            onOutput, onError, cancellationToken).ConfigureAwait(false);

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
