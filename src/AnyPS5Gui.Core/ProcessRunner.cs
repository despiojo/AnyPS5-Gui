using System.Diagnostics;
using System.Text;

namespace AnyPS5Gui.Core;

public sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments)
{
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

/// <summary>Runs a console program without a window and hands over its output line by line.</summary>
public static class ProcessRunner
{
    /// <summary>
    /// Returns the exit code. Cancelling kills the process and everything it started, then throws
    /// <see cref="OperationCanceledException"/>. The callbacks run on thread-pool threads.
    /// </summary>
    public static async Task<int> RunAsync(ProcessSpec spec, Action<string> onOutput, Action<string> onError,
        CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(spec.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (spec.WorkingDirectory is not null) start.WorkingDirectory = spec.WorkingDirectory;

        // A list, not a joined string: paths with spaces and values such as $ORIGIN/libs arrive intact.
        foreach (var argument in spec.Arguments) start.ArgumentList.Add(argument);
        if (spec.Environment is not null)
            foreach (var (name, value) in spec.Environment) start.Environment[name] = value;

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutput(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onError(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using (cancellationToken.Register(() =>
               {
                   try { process.Kill(entireProcessTree: true); }
                   catch (InvalidOperationException) { /* already gone */ }
               }))
        {
            // Also waits for both streams to end, so no line arrives after this returns.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }
}
