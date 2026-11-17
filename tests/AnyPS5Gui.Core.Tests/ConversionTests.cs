using AnyPS5Gui.Core.Conversion;
using AnyPS5Gui.Core.Games;
using AnyPS5Gui.Core.Relinker;

namespace AnyPS5Gui.Core.Tests;

public class ConversionTests
{
    /// <summary>
    /// Stands in for the relinker: writes the output executable (its last argument) and a converted
    /// module under app0/, as the real one does, and says how long the input (the argument before it)
    /// is. "--dry-run" writes nothing and "--fail" exits with 2.
    /// </summary>
    private const string FakeRelinker = """
        @echo off
        setlocal
        set "last="
        set "prev="
        set "dry="
        :next
        if "%~1"=="" goto run
        if "%~1"=="--fail" (
          echo FAIL: asked to fail 1>&2
          exit /b 2
        )
        if "%~1"=="--dry-run" set "dry=1"
        set "prev=%last%"
        set "last=%~1"
        shift
        goto next
        :run
        echo Converting to %last%
        for %%f in ("%prev%") do echo Read %%~zf bytes from %prev%
        if defined dry exit /b 0
        > "%last%" echo exe
        for %%f in ("%last%") do set "dir=%%~dpf"
        mkdir "%dir%app0\sce_module" 2>nul
        > "%dir%app0\sce_module\libc.guest.prx" echo converted
        exit /b 0
        """;

    private sealed class Fixture : IDisposable
    {
        public TempDirectory Temp { get; } = new();
        public string Game => Temp.Combine("dump", "My Game");
        public string Libs => Temp.Combine("store", "libs");
        public string Output => Temp.Combine("out", "My Game");
        public List<string> Lines { get; } = [];
        public List<string> Errors { get; } = [];

        public Fixture()
        {
            Temp.Write("relinker.cmd", FakeRelinker);
            Temp.WriteBytes(@"dump\My Game\eboot.bin", Magic.Elf);
            Temp.WriteBytes(@"dump\My Game\sce_module\libc.prx", Magic.Elf);
            Temp.Write(@"dump\My Game\sce_sys\param.json", "{\"titleId\":\"PPSA01234\"}");
            Temp.Write(@"dump\My Game\data\level 1\map.bin", "map");
            Temp.Write(@"dump\My Game\relinker-patches.toml", "format = 1");
            // A real dump is older than its conversion; files written during one are treated differently.
            foreach (var file in Directory.GetFiles(Game, "*", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(file, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Temp.Write(@"store\libs\libkernel.prx", "kernel");
            Temp.Write(@"store\libs\licenses\mimalloc.txt", "license");
        }

        public ConversionRequest Request(RelinkerOptions? options = null, string? output = null) => new()
        {
            Game = GameFolder.Inspect(Game),
            OutputDirectory = output ?? Output,
            ExecutableName = "PPSA01234.exe",
            Options = options ?? new RelinkerOptions(),
            RelinkerPath = Temp.Combine("relinker.cmd"),
            LibsPath = Libs,
        };

        public Task<ConversionResult> Run(ConversionRequest request, IProgress<ConversionProgress>? progress = null)
        {
            void Add(List<string> list, string line) { lock (list) list.Add(line); }
            return Converter.RunAsync(request, line => Add(Lines, line), line => Add(Errors, line), progress,
                TestContext.Current.CancellationToken);
        }

        /// <summary>The dump as it usually comes: executable and module as SELF containers, beside a module that is not one.</summary>
        public void Wrap(ulong executable = FakeSelf.Plain, bool wrapExecutable = true)
        {
            if (wrapExecutable) Temp.WriteBytes(@"dump\My Game\eboot.bin", FakeSelf.Build(executable));
            Temp.WriteBytes(@"dump\My Game\sce_module\libc.prx", FakeSelf.Build(version: true));
            Temp.WriteBytes(@"dump\My Game\sce_module\plain.prx", [.. Magic.Elf, 1, 2, 3]);
            foreach (var file in Directory.GetFiles(Game, "*", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(file, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        /// <summary>Every file of the dump with its contents and its time, to tell whether a conversion touched it.</summary>
        public Dictionary<string, string> Dump() =>
            Directory.GetFiles(Game, "*", SearchOption.AllDirectories).ToDictionary(
                file => Path.GetRelativePath(Game, file),
                file => $"{Convert.ToHexString(File.ReadAllBytes(file))} {File.GetLastWriteTimeUtc(file):O} {File.GetAttributes(file)}");

        public string App0(params string[] parts) => Path.Combine([Output, "app0", .. parts]);

        public void Dispose() => Temp.Dispose();
    }

    private sealed class Collector : IProgress<ConversionProgress>
    {
        public List<ConversionProgress> Reports { get; } = [];
        public void Report(ConversionProgress value) { lock (Reports) Reports.Add(value); }
    }

    [Fact]
    public async Task A_conversion_lays_out_the_executable_libs_and_app0()
    {
        using var fixture = new Fixture();
        var progress = new Collector();

        var result = await fixture.Run(fixture.Request(), progress);

        Assert.True(result.Succeeded);
        Assert.Equal(["--windows", Path.Combine(fixture.Game, "eboot.bin"), Path.Combine(fixture.Output, "PPSA01234.exe")],
            result.Command);
        Assert.Contains(fixture.Lines, line => line.StartsWith("Converting to"));

        Assert.True(File.Exists(Path.Combine(fixture.Output, "PPSA01234.exe")));
        Assert.Equal("kernel", File.ReadAllText(Path.Combine(fixture.Output, "libs", "libkernel.prx")));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "libs", "licenses", "mimalloc.txt")));
        Assert.Equal("map", File.ReadAllText(Path.Combine(fixture.Output, "app0", "data", "level 1", "map.bin")));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "app0", "eboot.bin")));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "app0", "sce_module", "libc.prx")));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "app0", "sce_sys", "param.json")));
        Assert.StartsWith("converted", File.ReadAllText(Path.Combine(fixture.Output, "app0", "sce_module", "libc.guest.prx")));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "app0", "relinker-patches.toml")));

        Assert.Equal(2, result.Libraries!.Total);
        Assert.Equal(4, result.GameFiles!.Total);
        Assert.Equal(0, result.GameFiles.Skipped);
        Assert.Equal([ConversionStage.Relinking, ConversionStage.Libraries, ConversionStage.GameFiles],
            progress.Reports.Select(report => report.Stage).Distinct());
        Assert.Contains(new ConversionProgress(ConversionStage.GameFiles, 4, 4), progress.Reports);
    }

    [Fact]
    public async Task Converting_again_refreshes_what_changed_and_keeps_the_rest()
    {
        using var fixture = new Fixture();
        await fixture.Run(fixture.Request());

        // The dump gets a new file and a replaced one, and happens to hold a file named like a converted module.
        var map = Path.Combine(fixture.Game, "data", "level 1", "map.bin");
        File.Delete(map);
        File.WriteAllText(map, "map v2");
        File.WriteAllText(Path.Combine(fixture.Game, "data", "new.bin"), "new");
        var decoy = Path.Combine(fixture.Game, "sce_module", "libc.guest.prx");
        File.WriteAllText(decoy, "from the dump");
        File.SetLastWriteTimeUtc(decoy, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        // Another AnyPS5 version is another file, not the linked one rewritten in place.
        File.Delete(Path.Combine(fixture.Libs, "libkernel.prx"));
        File.WriteAllText(Path.Combine(fixture.Libs, "libkernel.prx"), "kernel of a newer version");

        var result = await fixture.Run(fixture.Request());

        Assert.True(result.Succeeded);
        Assert.Equal(6, result.GameFiles!.Total);
        Assert.Equal(4, result.GameFiles.Skipped);
        Assert.Equal("map v2", File.ReadAllText(Path.Combine(fixture.Output, "app0", "data", "level 1", "map.bin")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(fixture.Output, "app0", "data", "new.bin")));
        Assert.StartsWith("converted", File.ReadAllText(Path.Combine(fixture.Output, "app0", "sce_module", "libc.guest.prx")));
        Assert.Equal("from the dump", File.ReadAllText(decoy));

        Assert.Equal(1, result.Libraries!.Skipped);
        Assert.Equal("kernel of a newer version", File.ReadAllText(Path.Combine(fixture.Output, "libs", "libkernel.prx")));
    }

    [Fact]
    public async Task A_failed_relinker_stops_before_any_file_is_placed()
    {
        using var fixture = new Fixture();

        var result = await fixture.Run(fixture.Request(new RelinkerOptions { ExtraArguments = "--fail" }));

        Assert.Equal(2, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.Null(result.GameFiles);
        Assert.Contains("FAIL: asked to fail", fixture.Errors.Select(line => line.Trim()));
        Assert.False(Directory.Exists(Path.Combine(fixture.Output, "app0")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Output, "libs")));
    }

    [Fact]
    public async Task A_dry_run_writes_nothing()
    {
        using var fixture = new Fixture();

        var result = await fixture.Run(fixture.Request(new RelinkerOptions { DryRun = true }));

        Assert.True(result.Succeeded);
        Assert.Contains("--dry-run", result.Command);
        Assert.Null(result.GameFiles);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task A_wrapped_game_is_unwrapped_into_the_output_and_the_dump_is_left_alone()
    {
        using var fixture = new Fixture();
        fixture.Wrap();
        var dump = fixture.Dump();
        var progress = new Collector();

        var result = await fixture.Run(fixture.Request(), progress);

        Assert.True(result.Succeeded);
        // The relinker reads the unwrapped executable, and is told where the dump's patches are.
        Assert.Equal(
            ["--windows", "--patches", Path.Combine(fixture.Game, "relinker-patches.toml"),
                fixture.App0("eboot.bin"), Path.Combine(fixture.Output, "PPSA01234.exe")],
            result.Command);
        Assert.Contains($"Read 520 bytes from {fixture.App0("eboot.bin")}", fixture.Lines.Select(line => line.Trim()));
        Assert.Equal(ConversionStage.Unwrapping, progress.Reports[0].Stage);

        // What the game runs with is the unwrapped files, not the containers placed over them afterwards.
        Assert.Equal(Magic.Elf[..4], File.ReadAllBytes(fixture.App0("eboot.bin"))[..4]);
        Assert.Equal(0x208, new FileInfo(fixture.App0("eboot.bin")).Length);
        Assert.Equal(0x216, new FileInfo(fixture.App0("sce_module", "libc.prx")).Length);
        Assert.Equal([.. Magic.Elf, 1, 2, 3], File.ReadAllBytes(fixture.App0("sce_module", "plain.prx")));
        Assert.StartsWith("converted", File.ReadAllText(fixture.App0("sce_module", "libc.guest.prx")));
        Assert.Equal("map", File.ReadAllText(fixture.App0("data", "level 1", "map.bin")));
        Assert.Empty(Directory.GetFiles(fixture.Output, "*.part", SearchOption.AllDirectories));

        Assert.Equal(dump, fixture.Dump());

        // Converting again does the same over what is there.
        Assert.True((await fixture.Run(fixture.Request())).Succeeded);
        Assert.Equal(0x208, new FileInfo(fixture.App0("eboot.bin")).Length);
        Assert.Equal(dump, fixture.Dump());

        // Patches switched off stay off.
        var withoutPatches = Converter.BuildArguments(fixture.Request(new RelinkerOptions { PatchMode = PatchMode.None }));
        Assert.Contains("--no-patches", withoutPatches);
        Assert.DoesNotContain("--patches", withoutPatches);
    }

    [Fact]
    public async Task A_module_linked_to_the_dump_by_an_earlier_conversion_is_replaced_and_not_written_through()
    {
        using var fixture = new Fixture();
        fixture.Wrap(wrapExecutable: false);
        // As a conversion from before containers were unwrapped left it: the dump's own file under a second name.
        Directory.CreateDirectory(fixture.App0("sce_module"));
        Assert.True(FileLinker.TryCreateHardLink(fixture.App0("sce_module", "libc.prx"),
            Path.Combine(fixture.Game, "sce_module", "libc.prx")));
        var dump = fixture.Dump();

        var result = await fixture.Run(fixture.Request());

        Assert.True(result.Succeeded);
        Assert.Equal(fixture.App0("eboot.bin"), result.Command[^2]);
        Assert.Equal(0x216, new FileInfo(fixture.App0("sce_module", "libc.prx")).Length);
        Assert.Equal(dump, fixture.Dump());
    }

    [Fact]
    public async Task A_relinker_that_fails_on_a_wrapped_game_leaves_only_what_was_unwrapped()
    {
        using var fixture = new Fixture();
        fixture.Wrap();
        var dump = fixture.Dump();

        var result = await fixture.Run(fixture.Request(new RelinkerOptions { ExtraArguments = "--fail" }));

        Assert.Equal(2, result.ExitCode);
        Assert.Null(result.GameFiles);
        // The relinker had to be given something to read: that stays, and the rest of the game is not placed.
        Assert.Equal(
            ["eboot.bin", @"sce_module\libc.prx", @"sce_module\plain.prx"],
            Directory.GetFiles(fixture.App0(), "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(fixture.App0(), file)).Order());
        Assert.False(Directory.Exists(Path.Combine(fixture.Output, "libs")));
        Assert.Equal(dump, fixture.Dump());
    }

    [Fact]
    public async Task A_dry_run_of_a_wrapped_game_writes_nothing_and_leaves_no_copy_behind()
    {
        using var fixture = new Fixture();
        fixture.Wrap();
        var dump = fixture.Dump();

        var result = await fixture.Run(fixture.Request(new RelinkerOptions { DryRun = true }));

        Assert.True(result.Succeeded);
        Assert.False(Directory.Exists(fixture.Output));
        Assert.Equal(dump, fixture.Dump());

        // It read an unwrapped copy from somewhere that is neither the dump nor the output, and that is gone.
        var input = result.Command[^2];
        Assert.Contains($"Read 520 bytes from {input}", fixture.Lines.Select(line => line.Trim()));
        Assert.False(Converter.IsSameOrInside(input, fixture.Temp.Path));
        Assert.False(Directory.Exists(Path.GetDirectoryName(input)));
    }

    [Fact]
    public async Task A_container_that_cannot_be_unwrapped_stops_the_conversion_before_the_relinker()
    {
        using var fixture = new Fixture();
        fixture.Wrap(FakeSelf.Encrypted);
        var dump = fixture.Dump();

        var failure = await Assert.ThrowsAsync<ConversionException>(() => fixture.Run(fixture.Request()));

        Assert.Equal(ConversionProblem.EncryptedContainer, failure.Problem);
        Assert.Equal("eboot.bin", failure.Detail);
        Assert.Empty(fixture.Lines);
        Assert.False(File.Exists(Path.Combine(fixture.Output, "PPSA01234.exe")));
        Assert.False(File.Exists(fixture.App0("eboot.bin")));
        Assert.Equal(dump, fixture.Dump());
    }

    [Fact]
    public async Task Output_and_game_folders_may_not_contain_each_other()
    {
        using var fixture = new Fixture();

        var inside = await Assert.ThrowsAsync<ConversionException>(
            () => fixture.Run(fixture.Request(output: Path.Combine(fixture.Game, "out"))));
        var same = await Assert.ThrowsAsync<ConversionException>(() => fixture.Run(fixture.Request(output: fixture.Game)));
        var around = await Assert.ThrowsAsync<ConversionException>(
            () => fixture.Run(fixture.Request(output: fixture.Temp.Combine("dump"))));

        Assert.Equal(ConversionProblem.OutputInsideGame, inside.Problem);
        Assert.Equal(ConversionProblem.OutputInsideGame, same.Problem);
        Assert.Equal(ConversionProblem.GameInsideOutput, around.Problem);
        Assert.False(Directory.Exists(Path.Combine(fixture.Game, "out")));
    }

    [Fact]
    public async Task Requests_that_cannot_start()
    {
        using var fixture = new Fixture();

        var noRelinker = await Assert.ThrowsAsync<ConversionException>(
            () => fixture.Run(fixture.Request() with { RelinkerPath = fixture.Temp.Combine("missing.exe") }));
        var noExecutable = await Assert.ThrowsAsync<ConversionException>(
            () => fixture.Run(fixture.Request() with { Game = GameFolder.Inspect(fixture.Temp.Combine("store")) }));

        Assert.Equal(ConversionProblem.RelinkerMissing, noRelinker.Problem);
        Assert.Equal(ConversionProblem.NoExecutable, noExecutable.Problem);
    }

    [Fact]
    public void Mirror_links_files_and_falls_back_to_copies()
    {
        using var temp = new TempDirectory();
        var source = temp.Combine("source");
        temp.Write(@"source\a.bin", "a");
        temp.Write(@"source\sub\b.bin", "b");
        temp.Write(@"source\skip.me", "s");
        File.SetAttributes(temp.Write(@"source\hidden.bin", "h"), FileAttributes.Hidden);
        // At the destination already: one file written just now, which is kept, and one stale one, which is not.
        temp.Write(@"linked\sub\b.bin", "kept");
        File.SetLastWriteTimeUtc(temp.Write(@"linked\a.bin", "stale copy"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var before = DateTime.UtcNow - TimeSpan.FromMinutes(1);

        var linked = FileLinker.Mirror(source, temp.Combine("linked"), preferLinks: true,
            exclude: relative => relative == "skip.me", keepNewerThanUtc: before,
            cancellationToken: TestContext.Current.CancellationToken);
        var copied = FileLinker.Mirror(source, temp.Combine("copied"), preferLinks: false,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new MirrorResult(Linked: 0, Copied: 4, Skipped: 0), copied);
        Assert.Equal(1, linked.Skipped);
        Assert.Equal(2, linked.Linked + linked.Copied);
        Assert.Equal("a", File.ReadAllText(temp.Combine("linked", "a.bin")));
        Assert.Equal("kept", File.ReadAllText(temp.Combine("linked", "sub", "b.bin")));
        Assert.Equal("h", File.ReadAllText(temp.Combine("linked", "hidden.bin")));
        Assert.False(File.Exists(temp.Combine("linked", "skip.me")));

        // A hard link is the same file under a second name; a copy is not.
        File.AppendAllText(temp.Combine("source", "a.bin"), "+");
        Assert.Equal("a", File.ReadAllText(temp.Combine("copied", "a.bin")));
        if (linked.Linked > 0) Assert.Equal("a+", File.ReadAllText(temp.Combine("linked", "a.bin")));
    }

    [Theory]
    [InlineData(@"C:\a\b", @"C:\a", true)]
    [InlineData(@"C:\A\", @"c:\a", true)]
    [InlineData(@"C:\ab", @"C:\a", false)]
    [InlineData(@"C:\a", @"C:\a\b", false)]
    public void IsSameOrInside(string path, string container, bool expected)
    {
        Assert.Equal(expected, Converter.IsSameOrInside(path, container));
    }
}
