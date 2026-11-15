using AnyPS5Gui.Core.Launch;

namespace AnyPS5Gui.Core.Tests;

public class LaunchSettingsTests
{
    [Fact]
    public void Defaults_set_nothing()
    {
        Assert.Empty(new LaunchSettings().ToEnvironment());
    }

    [Fact]
    public void Every_setting_maps_to_its_variable()
    {
        var settings = new LaunchSettings
        {
            Fullscreen = true, DisplayIndex = 1, Gpu = " RTX ", PresentMode = PresentMode.FifoLatestReady,
            FpsLimit = 59.94, Fsr = true, FsrSharpness = 1, OutputResolution = "1920x1080", Language = "auto",
            Volume = 50, MuteOnFocusLoss = true, Rumble = 0, DisableShaderCache = true, HostReport = true,
            Mimalloc = true, DisableDozen = true,
        };

        Assert.Equal(new Dictionary<string, string>
        {
            ["ANYPS5_FULLSCREEN"] = "1",
            ["ANYPS5_DISPLAY_INDEX"] = "1",
            ["ANYPS5_GPU"] = "RTX",
            ["ANYPS5_PRESENT_MODE"] = "fifo_latest_ready",
            ["ANYPS5_FPS_LIMIT"] = "59.94",
            ["ANYPS5_UPSCALE"] = "fsr",
            ["ANYPS5_FSR_SHARPNESS"] = "1.0",
            ["APS5_OUTPUT_RESOLUTION"] = "1920x1080",
            ["ANYPS5_LANGUAGE"] = "auto",
            ["ANYPS5_VOLUME"] = "50",
            ["ANYPS5_MUTE_ON_FOCUS_LOSS"] = "1",
            ["ANYPS5_RUMBLE"] = "0",
            ["ANYPS5_NO_SHADER_CACHE"] = "1",
            ["ANYPS5_HOST_REPORT"] = "1",
            ["APS5_MIMALLOC"] = "1",
            ["VK_LOADER_DRIVERS_DISABLE"] = "*dzn*",
        }, settings.ToEnvironment());
    }

    [Theory]
    [InlineData(30, "30")]
    [InlineData(59.94, "59.94")]
    [InlineData(23.9761, "23.976")]
    public void Fps_limit_keeps_at_most_three_decimals_whatever_the_culture(double limit, string expected)
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("es-ES");
        try
        {
            Assert.Equal(expected, new LaunchSettings { FpsLimit = limit }.ToEnvironment()["ANYPS5_FPS_LIMIT"]);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Fsr_sharpness_only_counts_with_fsr_and_away_from_its_default()
    {
        Assert.Empty(new LaunchSettings { FsrSharpness = 1.5 }.ToEnvironment());
        Assert.Equal(new[] { "ANYPS5_UPSCALE" }, new LaunchSettings { Fsr = true }.ToEnvironment().Keys);
        Assert.Equal("2.0", new LaunchSettings { Fsr = true, FsrSharpness = 9 }.ToEnvironment()["ANYPS5_FSR_SHARPNESS"]);
    }

    [Fact]
    public void Custom_lines_win_and_tolerate_comments()
    {
        var settings = new LaunchSettings
        {
            Fullscreen = true,
            CustomEnvironment = "# a note\r\nANYPS5_FULLSCREEN = borderless\n\nAPS5_NO_WGI\nAPS5_TRACE_DIR=C:\\a=b\n=ignored",
        };

        Assert.Equal(new Dictionary<string, string>
        {
            ["ANYPS5_FULLSCREEN"] = "borderless",
            ["APS5_NO_WGI"] = "",
            ["APS5_TRACE_DIR"] = @"C:\a=b",
        }, settings.ToEnvironment());
    }

    [Fact]
    public void Every_console_language_has_a_name()
    {
        Assert.Equal(31, ConsoleLanguages.Names.Count);
        Assert.Equal("English (United States)", ConsoleLanguages.Names[1]);
        Assert.Equal("Arabic", ConsoleLanguages.Names[21]);
    }
}
