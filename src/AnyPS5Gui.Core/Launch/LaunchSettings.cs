using System.Globalization;

namespace AnyPS5Gui.Core.Launch;

public enum PresentMode { Default, FifoLatestReady, Mailbox, Immediate }

/// <summary>
/// How one game is started. AnyPS5 is configured through environment variables; each property maps to
/// one of them and is only set when it differs from AnyPS5's own default.
/// </summary>
public sealed class LaunchSettings
{
    public bool Fullscreen { get; set; }
    /// <summary>Display the window opens on, counted from 0. Null: the first one.</summary>
    public int? DisplayIndex { get; set; }
    /// <summary>Part of a GPU name. Empty: AnyPS5 picks, preferring a discrete GPU.</summary>
    public string? Gpu { get; set; }
    public PresentMode PresentMode { get; set; }
    /// <summary>Frames per second, 0 for no limit.</summary>
    public double FpsLimit { get; set; }
    public bool Fsr { get; set; }
    public double FsrSharpness { get; set; } = DefaultFsrSharpness;
    /// <summary><c>WIDTHxHEIGHT</c> the game is told the display has. Empty: the display's own mode.</summary>
    public string? OutputResolution { get; set; }
    /// <summary><c>auto</c> or a console language number. Empty: English (United States).</summary>
    public string? Language { get; set; }
    public int Volume { get; set; } = 100;
    public bool MuteOnFocusLoss { get; set; }
    public int Rumble { get; set; } = 100;
    public bool DisableShaderCache { get; set; }
    public bool HostReport { get; set; }
    public bool Mimalloc { get; set; }
    /// <summary>Keeps the Vulkan loader away from the D3D12 "Dozen" driver, which crashes on some systems.</summary>
    public bool DisableDozen { get; set; }
    /// <summary><c>NAME=VALUE</c> lines for anything else. They win over the settings above.</summary>
    public string? CustomEnvironment { get; set; }
    /// <summary>Arguments passed to the game itself.</summary>
    public string? Arguments { get; set; }

    public const double DefaultFsrSharpness = 0.2;

    public Dictionary<string, string> ToEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Fullscreen) environment["ANYPS5_FULLSCREEN"] = "1";
        if (DisplayIndex is >= 0) environment["ANYPS5_DISPLAY_INDEX"] = Number(DisplayIndex.Value);
        if (!string.IsNullOrWhiteSpace(Gpu)) environment["ANYPS5_GPU"] = Gpu.Trim();

        var presentMode = PresentMode switch
        {
            PresentMode.FifoLatestReady => "fifo_latest_ready",
            PresentMode.Mailbox => "mailbox",
            PresentMode.Immediate => "immediate",
            _ => null,
        };
        if (presentMode is not null) environment["ANYPS5_PRESENT_MODE"] = presentMode;

        // AnyPS5 accepts at most three decimals.
        if (FpsLimit > 0) environment["ANYPS5_FPS_LIMIT"] = FpsLimit.ToString("0.###", CultureInfo.InvariantCulture);

        if (Fsr)
        {
            environment["ANYPS5_UPSCALE"] = "fsr";
            var sharpness = Math.Clamp(FsrSharpness, 0, 2);
            if (Math.Abs(sharpness - DefaultFsrSharpness) > 0.001)
                environment["ANYPS5_FSR_SHARPNESS"] = sharpness.ToString("0.0#", CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(OutputResolution)) environment["APS5_OUTPUT_RESOLUTION"] = OutputResolution.Trim();
        if (!string.IsNullOrWhiteSpace(Language)) environment["ANYPS5_LANGUAGE"] = Language.Trim();
        if (Volume is >= 0 and < 100) environment["ANYPS5_VOLUME"] = Number(Volume);
        if (MuteOnFocusLoss) environment["ANYPS5_MUTE_ON_FOCUS_LOSS"] = "1";
        if (Rumble is >= 0 and < 100) environment["ANYPS5_RUMBLE"] = Number(Rumble);
        if (DisableShaderCache) environment["ANYPS5_NO_SHADER_CACHE"] = "1";
        if (HostReport) environment["ANYPS5_HOST_REPORT"] = "1";
        if (Mimalloc) environment["APS5_MIMALLOC"] = "1";
        if (DisableDozen) environment["VK_LOADER_DRIVERS_DISABLE"] = "*dzn*";

        foreach (var (name, value) in ParseEnvironment(CustomEnvironment)) environment[name] = value;
        return environment;
    }

    /// <summary>Reads <c>NAME=VALUE</c> lines. Blank lines and lines starting with <c>#</c> are skipped; a bare name sets an empty value.</summary>
    public static IEnumerable<KeyValuePair<string, string>> ParseEnvironment(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var separator = line.IndexOf('=');
            var name = (separator < 0 ? line : line[..separator]).Trim();
            if (name.Length == 0) continue;
            yield return new(name, separator < 0 ? "" : line[(separator + 1)..].Trim());
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
