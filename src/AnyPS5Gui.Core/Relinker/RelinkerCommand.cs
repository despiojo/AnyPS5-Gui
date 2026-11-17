using System.Text;

namespace AnyPS5Gui.Core.Relinker;

/// <summary>Turns <see cref="RelinkerOptions"/> into the relinker's argument list.</summary>
public static class RelinkerCommand
{
    /// <param name="patchFile">
    /// The patch file to name when the options leave finding it to the relinker, which looks beside the
    /// input: for an input that is not where the game is.
    /// </param>
    public static List<string> Build(RelinkerOptions options, string input, string output, string? patchFile = null)
    {
        // The format defaults to Linux whatever the output is called: ".exe" alone does not select Windows.
        var arguments = new List<string> { "--windows" };

        if (options.DryRun) arguments.Add("--dry-run");
        if (options.WindowsGui) arguments.Add("--windows-gui");
        if (options.WindowsDiagnostics) arguments.Add("--windows-diagnostics");
        if (options.ToIntel) arguments.Add("--to-intel");
        if (options.FrameRedZone) arguments.Add("--frame-red-zone");
        if (options.TraceImports) arguments.Add("--trace-imports");
        if (options.Registry) arguments.Add("--registry");
        if (options.FunctionSymbolRoots) arguments.Add("--function-symbol-roots");

        // The only switch written without "--".
        if (options.UnusedFilter is 1 or 2) arguments.Add($"unused-filter={options.UnusedFilter}");

        AddValue(arguments, "--windows-fixed-base", options.WindowsFixedBase);
        AddValue(arguments, "--rpath", options.Rpath);
        AddValue(arguments, "--sce-module-path", options.SceModulePath);
        foreach (var directory in Clean(options.ModuleDirectories)) AddValue(arguments, "--module-dir", directory);
        foreach (var executable in Clean(options.Executables)) AddValue(arguments, "--executable", executable);

        switch (options.PatchMode)
        {
            case PatchMode.Auto:
                AddValue(arguments, "--patches", patchFile);
                break;
            case PatchMode.None:
                arguments.Add("--no-patches");
                break;
            case PatchMode.File:
                AddValue(arguments, "--patches", options.PatchFile);
                break;
        }

        arguments.AddRange(SplitArguments(options.ExtraArguments));
        arguments.Add(input);
        arguments.Add(output);
        return arguments;
    }

    /// <summary>The command as it would be typed, for showing to the user. Not used to start the process.</summary>
    public static string Format(string program, IEnumerable<string> arguments)
    {
        var text = new StringBuilder(Quote(program));
        foreach (var argument in arguments) text.Append(' ').Append(Quote(argument));
        return text.ToString();
    }

    /// <summary>Splits free text into arguments: whitespace separates, double quotes group, <c>\"</c> is a literal quote.</summary>
    public static List<string> SplitArguments(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var current = new StringBuilder();
        var inQuotes = false;
        var started = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == '"')
            {
                current.Append('"');
                started = true;
                i++;
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
                started = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (started) result.Add(current.ToString());
                current.Clear();
                started = false;
            }
            else
            {
                current.Append(c);
                started = true;
            }
        }
        if (started) result.Add(current.ToString());
        return result;
    }

    /// <summary>One entry per non-empty line.</summary>
    public static List<string> SplitLines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static void AddValue(List<string> arguments, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        arguments.Add(name);
        arguments.Add(value.Trim());
    }

    private static IEnumerable<string> Clean(IEnumerable<string> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value));

    private static string Quote(string argument) =>
        argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"')
            ? argument
            : '"' + argument.Replace("\"", "\\\"") + '"';
}
