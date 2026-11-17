using System.Buffers.Binary;
using AnyPS5Gui.Core.Games;

namespace AnyPS5Gui.Core.Tests;

/// <summary>
/// A SELF container built by hand, small enough to read in full: one loadable segment of eight bytes,
/// stored at 0x1A0 in the container and belonging at 0x200 in the executable, and a second program
/// header whose data is part of that segment. On request, a third for the version data, which a container
/// keeps at its very end, and a fourth for a note the container does not hold.
/// </summary>
public static class FakeSelf
{
    public const ulong Plain = 0x2804;
    public const ulong Encrypted = 0x2806;
    public const ulong Compressed = 0x280C;

    /// <param name="data">Properties of the entry that holds the segment.</param>
    /// <param name="segment">The program header that entry says it belongs to.</param>
    public static byte[] Build(ulong data = Plain, int segment = 0, bool ps4 = false, int stored = 8,
        bool version = false, bool lostNote = false)
    {
        var file = new byte[version ? 0x1AE : 0x1A8];
        var magic = ps4 ? new byte[] { 0x4F, 0x15, 0x3D, 0x1D } : [0x54, 0x14, 0xF5, 0xEE];
        magic.CopyTo(file, 0);
        new byte[] { 0x10, 0x01, 0x01, 0x12, 0x01, 0x01, 0x00, 0x10 }.CopyTo(file, 4);
        U16(file, 0x0C, 0x1A0);
        U64(file, 0x10, 0x1A8);
        U16(file, 0x18, 2);
        U16(file, 0x1A, 0x32);

        // The digests of the entry after it, then that entry: the segment itself.
        U64(file, 0x20, 0x110004); U64(file, 0x28, 0x180); U64(file, 0x30, 0x20); U64(file, 0x38, 0x20);
        U64(file, 0x40, data | (ulong)segment << 20); U64(file, 0x48, 0x1A0); U64(file, 0x50, (ulong)stored); U64(file, 0x58, 8);

        const int elf = 0x60;
        new byte[] { 0x7F, 0x45, 0x4C, 0x46, 2, 1, 1, 9 }.CopyTo(file, elf);
        U16(file, elf + 0x10, 2); U16(file, elf + 0x12, 0x3E); U32(file, elf + 0x14, 1);
        U64(file, elf + 0x18, 0x401234);
        U64(file, elf + 0x20, 0x40);        // program headers
        U64(file, elf + 0x28, 0x5000);      // section headers: not in the container
        U16(file, elf + 0x34, 0x40); U16(file, elf + 0x36, 0x38); U16(file, elf + 0x38, (ushort)(2 + (version ? 1 : 0) + (lostNote ? 1 : 0)));
        U16(file, elf + 0x3A, 0x40); U16(file, elf + 0x3C, 3); U16(file, elf + 0x3E, 2);

        const int load = elf + 0x40;
        U32(file, load, 1); U32(file, load + 4, 5);
        U64(file, load + 0x08, 0x200); U64(file, load + 0x10, 0x400000); U64(file, load + 0x18, 0x400000);
        U64(file, load + 0x20, 8); U64(file, load + 0x28, 8); U64(file, load + 0x30, 0x4000);

        const int dynamic = load + 0x38;
        U32(file, dynamic, 2); U32(file, dynamic + 4, 6);
        U64(file, dynamic + 0x08, 0x204); U64(file, dynamic + 0x20, 4); U64(file, dynamic + 0x28, 4);

        var next = dynamic + 0x38;
        if (version)
        {
            // Where the original executable had it, which is not where it ends up.
            U32(file, next, 0x6FFFFF01);
            U64(file, next + 0x08, 0x300); U64(file, next + 0x20, 6); U64(file, next + 0x28, 8); U64(file, next + 0x30, 0x10);
            "v1.2.3"u8.CopyTo(file.AsSpan(0x1A8));
            next += 0x38;
        }
        if (lostNote)
        {
            U32(file, next, 4);
            U64(file, next + 0x08, 0x400); U64(file, next + 0x20, 0x10); U64(file, next + 0x30, 4);
        }

        Array.Fill(file, (byte)0xEE, 0x180, 0x20);
        "ABCDEFGH"u8.CopyTo(file.AsSpan(0x1A0));
        return file;
    }

    private static void U16(byte[] file, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at), value);
    private static void U32(byte[] file, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), value);
    private static void U64(byte[] file, int at, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(at), value);
}

public class SelfContainerTests
{
    private static byte[] Unwrap(TempDirectory temp, byte[] container)
    {
        var source = temp.WriteBytes("eboot.bin", container);
        SelfContainer.Unwrap(source, temp.Combine("out", "eboot.elf"), TestContext.Current.CancellationToken);
        return File.ReadAllBytes(temp.Combine("out", "eboot.elf"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_container_is_unwrapped_into_the_executable_it_holds(bool ps4)
    {
        using var temp = new TempDirectory();

        var elf = Unwrap(temp, FakeSelf.Build(ps4: ps4));

        // As long as its last segment needs, and no longer.
        Assert.Equal(0x208, elf.Length);
        // The header, with the section table it no longer has taken out of it.
        Assert.Equal(
            "7F454C4602010109" + "0000000000000000" + "02003E0001000000" + "3412400000000000" +
            "4000000000000000" + "0000000000000000" + "0000000040003800" + "0200400000000000",
            Convert.ToHexString(elf, 0, 0x40));
        // Both program headers, as they were.
        Assert.Equal(
            "0100000005000000" + "0002000000000000" + "0000400000000000" + "0000400000000000" +
            "0800000000000000" + "0800000000000000" + "0040000000000000",
            Convert.ToHexString(elf, 0x40, 0x38));
        Assert.Equal(
            "0200000006000000" + "0402000000000000" + "0000000000000000" + "0000000000000000" +
            "0400000000000000" + "0400000000000000" + "0000000000000000",
            Convert.ToHexString(elf, 0x78, 0x38));
        // The segment where its program header says, and nothing of the container's own around it.
        Assert.All(elf[0xB0..0x200], value => Assert.Equal(0, value));
        Assert.Equal("ABCDEFGH", System.Text.Encoding.ASCII.GetString(elf, 0x200, 8));
    }

    [Fact]
    public void The_version_data_goes_after_the_last_segment_and_its_program_header_follows_it()
    {
        using var temp = new TempDirectory();

        var elf = Unwrap(temp, FakeSelf.Build(version: true));

        // The segments end at 0x208 and the version data is aligned to 0x10.
        Assert.Equal(0x216, elf.Length);
        Assert.Equal("v1.2.3", System.Text.Encoding.ASCII.GetString(elf, 0x210, 6));
        Assert.All(elf[0x208..0x210], value => Assert.Equal(0, value));
        Assert.Equal(
            "01FFFF6F00000000" + "1002000000000000" + "0000000000000000" + "0000000000000000" +
            "0600000000000000" + "0800000000000000" + "1000000000000000",
            Convert.ToHexString(elf, 0xB0, 0x38));
    }

    [Fact]
    public void A_program_header_whose_data_the_container_does_not_hold_points_at_nothing()
    {
        using var temp = new TempDirectory();

        var elf = Unwrap(temp, FakeSelf.Build(lostNote: true));

        Assert.Equal(0x208, elf.Length);
        Assert.Equal(
            "0400000000000000" + "0000000000000000" + "0000000000000000" + "0000000000000000" +
            "0000000000000000" + "0000000000000000" + "0400000000000000",
            Convert.ToHexString(elf, 0xB0, 0x38));
        // The one whose data is inside a segment keeps pointing at it.
        Assert.Equal("0402000000000000", Convert.ToHexString(elf, 0x80, 8));
    }

    [Fact]
    public void The_container_itself_is_left_as_it_was()
    {
        using var temp = new TempDirectory();
        var container = FakeSelf.Build();

        Unwrap(temp, container);

        Assert.Equal(container, File.ReadAllBytes(temp.Combine("eboot.bin")));
    }

    [Theory]
    [InlineData(FakeSelf.Encrypted, SelfProblem.Encrypted)]
    [InlineData(FakeSelf.Compressed, SelfProblem.Compressed)]
    public void A_segment_that_is_not_stored_as_it_is_cannot_be_unwrapped(ulong properties, SelfProblem expected)
    {
        using var temp = new TempDirectory();
        var source = temp.WriteBytes("eboot.bin", FakeSelf.Build(properties));

        var failure = Assert.Throws<SelfException>(() =>
            SelfContainer.Unwrap(source, temp.Combine("eboot.elf"), TestContext.Current.CancellationToken));

        Assert.Equal(expected, failure.Problem);
        Assert.Equal(source, failure.File);
        // Half an executable would be taken for a whole one.
        Assert.False(File.Exists(temp.Combine("eboot.elf")));
    }

    [Fact]
    public void A_container_that_does_not_add_up_is_refused()
    {
        using var temp = new TempDirectory();
        // The size of the version data is in its program header, the third: 0x60 + 0x40 + 2 * 0x38 + 0x20.
        static byte[] Lengthen(byte[] container) { container[0x131] = 0x10; return container; }
        var cases = new Dictionary<string, byte[]>
        {
            ["a segment for a program header that does not exist"] = FakeSelf.Build(segment: 2),
            ["more stored than the segment holds"] = FakeSelf.Build(stored: 9),
            ["cut short"] = FakeSelf.Build()[..0x1A4],
            ["version data longer than the container"] = Lengthen(FakeSelf.Build(version: true)),
            ["only a header"] = FakeSelf.Build()[..0x20],
            ["not a container"] = [.. Magic.Elf, .. new byte[0x200]],
            ["empty"] = [],
        };

        foreach (var (name, bytes) in cases)
        {
            var source = temp.WriteBytes($"{name}.bin", bytes);
            var failure = Assert.Throws<SelfException>(() =>
                SelfContainer.Unwrap(source, temp.Combine($"{name}.elf"), TestContext.Current.CancellationToken));
            Assert.Equal(SelfProblem.Malformed, failure.Problem);
            Assert.False(File.Exists(temp.Combine($"{name}.elf")), name);
        }
    }
}
