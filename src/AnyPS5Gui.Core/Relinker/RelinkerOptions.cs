namespace AnyPS5Gui.Core.Relinker;

public enum PatchMode
{
    /// <summary>Let the relinker pick up <c>relinker-patches.toml</c> beside the executable.</summary>
    Auto,
    /// <summary><c>--no-patches</c>.</summary>
    None,
    /// <summary><c>--patches &lt;file&gt;</c>.</summary>
    File,
}

/// <summary>The relinker switches the app exposes. The output is always a Windows executable.</summary>
public sealed class RelinkerOptions
{
    public bool DryRun { get; set; }
    public bool ToIntel { get; set; }
    public bool WindowsGui { get; set; }
    public bool WindowsDiagnostics { get; set; }
    public bool FrameRedZone { get; set; }
    public bool TraceImports { get; set; }
    public bool Registry { get; set; }
    public bool FunctionSymbolRoots { get; set; }

    /// <summary>0, 1 or 2. 0 is the relinker's default and is not passed.</summary>
    public int UnusedFilter { get; set; }

    public string? WindowsFixedBase { get; set; }
    public string? Rpath { get; set; }
    public string? SceModulePath { get; set; }
    public List<string> ModuleDirectories { get; set; } = [];
    public List<string> Executables { get; set; } = [];
    public PatchMode PatchMode { get; set; }
    public string? PatchFile { get; set; }

    /// <summary>Anything the app has no control for, written as on a command line.</summary>
    public string? ExtraArguments { get; set; }
}
