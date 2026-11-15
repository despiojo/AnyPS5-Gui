using AnyPS5Gui.Core.Relinker;

namespace AnyPS5Gui.Core.Tests;

public class RelinkerCommandTests
{
    private const string Input = @"D:\Dumps\Game\eboot.bin";
    private const string Output = @"D:\Out\Game\PPSA01234.exe";

    [Fact]
    public void Defaults_only_select_windows()
    {
        Assert.Equal(["--windows", Input, Output], RelinkerCommand.Build(new RelinkerOptions(), Input, Output));
    }

    [Fact]
    public void Switches_come_before_the_paths()
    {
        var options = new RelinkerOptions
        {
            DryRun = true, WindowsGui = true, WindowsDiagnostics = true, ToIntel = true, FrameRedZone = true,
            TraceImports = true, Registry = true, FunctionSymbolRoots = true,
        };

        Assert.Equal(
        [
            "--windows", "--dry-run", "--windows-gui", "--windows-diagnostics", "--to-intel", "--frame-red-zone",
            "--trace-imports", "--registry", "--function-symbol-roots", Input, Output,
        ], RelinkerCommand.Build(options, Input, Output));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "unused-filter=1")]
    [InlineData(2, "unused-filter=2")]
    [InlineData(7, null)]
    public void Unused_filter_has_no_dashes_and_is_left_out_at_its_default(int level, string? expected)
    {
        var arguments = RelinkerCommand.Build(new RelinkerOptions { UnusedFilter = level }, Input, Output);

        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--unused-filter"));
        if (expected is null) Assert.DoesNotContain(arguments, argument => argument.StartsWith("unused-filter"));
        else Assert.Contains(expected, arguments);
    }

    [Fact]
    public void Values_follow_their_option_and_repeat_per_entry()
    {
        var options = new RelinkerOptions
        {
            WindowsFixedBase = " 0x400000 ",
            Rpath = "$ORIGIN/libs",
            SceModulePath = @"D:\Dumps\Game Files",
            ModuleDirectories = ["Media/Plugins", " ", "."],
            Executables = ["picker/picker.bin"],
        };

        Assert.Equal(
        [
            "--windows", "--windows-fixed-base", "0x400000", "--rpath", "$ORIGIN/libs",
            "--sce-module-path", @"D:\Dumps\Game Files", "--module-dir", "Media/Plugins", "--module-dir", ".",
            "--executable", "picker/picker.bin", Input, Output,
        ], RelinkerCommand.Build(options, Input, Output));
    }

    [Fact]
    public void Patch_modes()
    {
        var auto = RelinkerCommand.Build(new RelinkerOptions { PatchFile = "ignored.toml" }, Input, Output);
        var none = RelinkerCommand.Build(new RelinkerOptions { PatchMode = PatchMode.None }, Input, Output);
        var file = RelinkerCommand.Build(new RelinkerOptions { PatchMode = PatchMode.File, PatchFile = @"D:\p.toml" }, Input, Output);
        var fileWithoutPath = RelinkerCommand.Build(new RelinkerOptions { PatchMode = PatchMode.File }, Input, Output);

        Assert.Equal(["--windows", Input, Output], auto);
        Assert.Equal(["--windows", "--no-patches", Input, Output], none);
        Assert.Equal(["--windows", "--patches", @"D:\p.toml", Input, Output], file);
        Assert.Equal(["--windows", Input, Output], fileWithoutPath);
    }

    [Fact]
    public void Extra_arguments_are_split_and_placed_last()
    {
        var options = new RelinkerOptions { ToIntel = true, ExtraArguments = "--exclude-sce-module \"lib x.prx\" --skip-syscall-check" };

        Assert.Equal(
            ["--windows", "--to-intel", "--exclude-sce-module", "lib x.prx", "--skip-syscall-check", Input, Output],
            RelinkerCommand.Build(options, Input, Output));
    }

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("a b\tc", new[] { "a", "b", "c" })]
    [InlineData("\"a b\" c", new[] { "a b", "c" })]
    [InlineData("--rpath=\"C:\\my libs\"", new[] { "--rpath=C:\\my libs" })]
    [InlineData("say \\\"hi\\\"", new[] { "say", "\"hi\"" })]
    [InlineData("\"\" x", new[] { "", "x" })]
    public void SplitArguments(string? text, string[] expected)
    {
        Assert.Equal(expected, RelinkerCommand.SplitArguments(text));
    }

    [Fact]
    public void SplitLines_drops_blank_lines_and_trims()
    {
        Assert.Equal(["Media/Plugins", "."], RelinkerCommand.SplitLines("  Media/Plugins \r\n\r\n.\n"));
        Assert.Empty(RelinkerCommand.SplitLines(null));
    }

    [Fact]
    public void Format_quotes_what_a_shell_would_split()
    {
        var text = RelinkerCommand.Format(@"C:\Tools\relinker.exe", ["--windows", @"D:\My Games\eboot.bin", ""]);

        Assert.Equal(@"C:\Tools\relinker.exe --windows ""D:\My Games\eboot.bin"" """"", text);
    }
}
