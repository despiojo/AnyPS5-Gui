using System.Buffers.Binary;
using AnyPS5Gui.Core.Conversion;
using AnyPS5Gui.Core.Games;
using AnyPS5Gui.Core.Relinker;

namespace AnyPS5Gui.Core.Tests;

/// <summary>
/// The whole conversion against AnyPS5's actual relinker. It needs a relinker.exe, named by the
/// ANYPS5_RELINKER environment variable; without one the tests are skipped.
/// </summary>
public class RealRelinkerTests
{
    private static string Relinker()
    {
        var relinker = Environment.GetEnvironmentVariable("ANYPS5_RELINKER");
        Assert.SkipUnless(File.Exists(relinker), "Set ANYPS5_RELINKER to AnyPS5's relinker.exe to run this test.");
        return relinker!;
    }

    private static ConversionRequest Request(TempDirectory temp, string relinker, RelinkerOptions? options = null)
    {
        temp.WriteBytes(@"dump\eboot.bin", ReturnsFortyTwo());
        Directory.CreateDirectory(temp.Combine("dump", "sce_module"));
        temp.Write(@"dump\sce_sys\param.json", "{\"titleId\":\"PPSA99999\"}");
        temp.Write(@"dump\data\level.bin", "level");
        temp.Write(@"libs\libkernel.prx", "stand-in");

        var game = GameFolder.Inspect(temp.Combine("dump"));
        Assert.Empty(game.Issues);
        return new ConversionRequest
        {
            Game = game,
            OutputDirectory = temp.Combine("out", "The Game"),
            ExecutableName = OutputNaming.ExecutableName(game),
            Options = options ?? new RelinkerOptions(),
            RelinkerPath = relinker,
            LibsPath = temp.Combine("libs"),
        };
    }

    [Fact]
    public async Task A_converted_game_is_laid_out_and_runs()
    {
        var relinker = Relinker();
        using var temp = new TempDirectory();
        var request = Request(temp, relinker, new RelinkerOptions { Registry = true, UnusedFilter = 1 });
        var output = new List<string>();
        void Collect(string line) { lock (output) output.Add(line); }

        var result = await Converter.RunAsync(request, Collect, Collect, null, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, output));
        Assert.Equal("MZ"u8.ToArray(), File.ReadAllBytes(request.OutputExecutable)[..2]);
        Assert.True(File.Exists(Path.Combine(request.OutputDirectory, "PPSA99999.registry.json")));
        Assert.Equal("stand-in", File.ReadAllText(Path.Combine(request.OutputDirectory, "libs", "libkernel.prx")));
        Assert.Equal("level", File.ReadAllText(Path.Combine(request.OutputDirectory, "app0", "data", "level.bin")));
        Assert.True(File.Exists(Path.Combine(request.OutputDirectory, "app0", "eboot.bin")));

        var exitCode = await ProcessRunner.RunAsync(
            new ProcessSpec(request.OutputExecutable, []) { WorkingDirectory = request.OutputDirectory },
            Collect, Collect, TestContext.Current.CancellationToken);
        Assert.Equal(42, exitCode);
    }

    [Fact]
    public async Task A_dry_run_converts_in_memory_only()
    {
        var relinker = Relinker();
        using var temp = new TempDirectory();
        var request = Request(temp, relinker, new RelinkerOptions { DryRun = true, WindowsGui = true });
        var output = new List<string>();
        void Collect(string line) { lock (output) output.Add(line); }

        var result = await Converter.RunAsync(request, Collect, Collect, null, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, output));
        Assert.False(Directory.Exists(temp.Combine("out")));
    }

    [Fact]
    public async Task What_the_relinker_refuses_is_reported_as_a_failure()
    {
        var relinker = Relinker();
        using var temp = new TempDirectory();
        var request = Request(temp, relinker, new RelinkerOptions { ExtraArguments = "--no-such-option" });
        var errors = new List<string>();

        var result = await Converter.RunAsync(request, _ => { }, line => { lock (errors) errors.Add(line); }, null,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(errors, line => line.Contains("--no-such-option"));
        Assert.False(File.Exists(request.OutputExecutable));
        Assert.Null(result.GameFiles);
    }

    /// <summary>
    /// A 4 KB executable that returns 42, with one relocation and no imports. It is the fixture of
    /// AnyPS5's own relinker tests (test_optional_plt.py), rebuilt here byte for byte.
    /// </summary>
    private static byte[] ReturnsFortyTwo()
    {
        var image = new byte[0x1000];
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(offset), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset), value);
        void U64(int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(offset), value);

        // ELF header: a 64-bit x86-64 shared object with two program headers and its entry at 0x200.
        new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1 }.CopyTo(image, 0);
        U16(16, 3); U16(18, 62); U32(20, 1); U64(24, 0x200); U64(32, 64);
        U16(52, 64); U16(54, 56); U16(56, 2); U16(58, 64);

        // jmp [rip+0xfa], through the pointer at 0x300 that the relocation below fills with 0x210.
        new byte[] { 0xFF, 0x25, 0xFA, 0, 0, 0 }.CopyTo(image, 0x200);
        // mov eax, 42; ret
        new byte[] { 0xB8, 0x2A, 0, 0, 0, 0xC3 }.CopyTo(image, 0x210);
        U64(0x700, 0x300); U64(0x708, 8); U64(0x710, 0x210);

        (ulong Tag, ulong Value)[] dynamic =
            [(5, 0x600), (10, 1), (6, 0x620), (11, 24), (7, 0x700), (8, 24), (9, 24), (0, 0)];
        var dynamicSize = (ulong)dynamic.Length * 16;

        // PT_LOAD covering the file, then PT_DYNAMIC.
        U32(64, 1); U32(68, 7); U64(96, 0x1000); U64(104, 0x1000); U64(112, 0x1000);
        U32(120, 2); U32(124, 6); U64(128, 0x400); U64(136, 0x400); U64(144, 0x400);
        U64(152, dynamicSize); U64(160, dynamicSize); U64(168, 8);
        for (var i = 0; i < dynamic.Length; i++)
        {
            U64(0x400 + i * 16, dynamic[i].Tag);
            U64(0x408 + i * 16, dynamic[i].Value);
        }
        return image;
    }
}
