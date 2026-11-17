using System.Buffers.Binary;

namespace AnyPS5Gui.Core.Games;

public enum SelfProblem
{
    /// <summary>The segments are encrypted: the dump was not decrypted, and this app holds no keys.</summary>
    Encrypted,
    /// <summary>The segments are compressed, which no dump tried so far does.</summary>
    Compressed,
    /// <summary>Not a container, or one whose tables point outside the file.</summary>
    Malformed,
}

/// <summary>A container that cannot be unwrapped. Reading or writing failing is an <see cref="IOException"/> as usual.</summary>
public sealed class SelfException(SelfProblem problem, string file)
    : Exception($"{Path.GetFileName(file)}: {problem}")
{
    public SelfProblem Problem { get; } = problem;
    public string File { get; } = file;
}

/// <summary>
/// A dumped executable or module usually comes as a SELF container: the console's wrapper around an
/// ordinary ELF, here with its signature faked and its contents in the clear. The relinker reads only
/// the ELF. The container is a table of entries, then the ELF's own header and program headers, then
/// the data of each segment; unwrapping puts each segment back where its program header says it goes.
/// <code>
/// 0x00  magic, version, sizes; the number of entries at 0x18
/// 0x20  entries, 0x20 bytes each: properties, offset in the container, size stored, size in memory
///       ELF header and program headers
///       ... the segments
/// </code>
/// Nothing is decrypted: a container with encrypted segments is refused.
/// </summary>
public static class SelfContainer
{
    private const int HeaderSize = 0x20;
    private const int EntrySize = 0x20;
    private const int ElfHeaderSize = 0x40;
    private const int ProgramHeaderSize = 0x38;

    // Properties of an entry. One with blocks is the data of a segment; the others are digests of one.
    private const ulong Encrypted = 1 << 1;
    private const ulong Compressed = 1 << 3;
    private const ulong HasBlocks = 1 << 11;
    private const int SegmentShift = 20;

    /// <summary>Further into the file than any segment of a real executable starts: 64 GiB.</summary>
    private const ulong MaxOffset = 1UL << 36;

    /// <summary>The program header type of the version data (PT_SCE_VERSION).</summary>
    private const uint VersionSegment = 0x6FFFFF01;

    private readonly record struct Segment(long From, long To, long Length);

    /// <summary>
    /// Writes the ELF inside <paramref name="source"/> to <paramref name="destination"/>, creating its
    /// folder. The destination only appears when it is complete, and the source is only read.
    /// </summary>
    public static void Unwrap(string source, string destination, CancellationToken cancellationToken = default)
    {
        using var input = File.OpenRead(source);
        SelfException Bad(SelfProblem problem = SelfProblem.Malformed) => new(problem, source);

        var header = Read(input, 0, HeaderSize) ?? throw Bad();
        if (!IsContainer(header)) throw Bad();
        int entryCount = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0x18));
        var entries = Read(input, HeaderSize, entryCount * EntrySize) ?? throw Bad();

        long elfOffset = HeaderSize + entryCount * EntrySize;
        var elf = Read(input, elfOffset, ElfHeaderSize) ?? throw Bad();
        // 64-bit and little-endian, which is all a console of these has.
        if (!elf.AsSpan(0, 6).SequenceEqual<byte>([0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1])) throw Bad();
        var programHeadersAt = BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(0x20));
        int programHeaderCount = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x38));
        if (BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x36)) != ProgramHeaderSize) throw Bad();
        if (programHeadersAt < ElfHeaderSize || programHeadersAt > (ulong)input.Length) throw Bad();
        var programHeaders = Read(input, elfOffset + (long)programHeadersAt, programHeaderCount * ProgramHeaderSize)
                             ?? throw Bad();

        var segments = new List<Segment>();
        var withEntry = new HashSet<int>();
        var headersEnd = (long)programHeadersAt + programHeaders.Length;
        var length = headersEnd;
        for (var i = 0; i < entryCount; i++)
        {
            var entry = entries.AsSpan(i * EntrySize, EntrySize);
            var properties = BinaryPrimitives.ReadUInt64LittleEndian(entry);
            if ((properties & HasBlocks) == 0) continue;
            if ((properties & Encrypted) != 0) throw Bad(SelfProblem.Encrypted);
            if ((properties & Compressed) != 0) throw Bad(SelfProblem.Compressed);

            var index = (int)(properties >> SegmentShift & 0xFFFF);
            if (index >= programHeaderCount) throw Bad();
            var programHeader = programHeaders.AsSpan(index * ProgramHeaderSize, ProgramHeaderSize);
            var to = BinaryPrimitives.ReadUInt64LittleEndian(programHeader[0x08..]);
            var room = BinaryPrimitives.ReadUInt64LittleEndian(programHeader[0x20..]);
            var from = BinaryPrimitives.ReadUInt64LittleEndian(entry[0x08..]);
            var stored = BinaryPrimitives.ReadUInt64LittleEndian(entry[0x10..]);
            if (stored > room || from > (ulong)input.Length || stored > (ulong)input.Length - from) throw Bad();
            // A segment over the headers, or one that would make a file no executable is the size of.
            if (to > MaxOffset || to < (ulong)headersEnd && stored > 0) throw Bad();

            withEntry.Add(index);
            segments.Add(new Segment((long)from, (long)to, (long)stored));
            length = Math.Max(length, (long)(to + stored));
        }

        // The version data has no entry: it is the last bytes of the container, and goes after the
        // last segment, where its program header is then pointed.
        for (var index = 0; index < programHeaderCount; index++)
        {
            var programHeader = programHeaders.AsSpan(index * ProgramHeaderSize, ProgramHeaderSize);
            var size = BinaryPrimitives.ReadUInt64LittleEndian(programHeader[0x20..]);
            if (BinaryPrimitives.ReadUInt32LittleEndian(programHeader) != VersionSegment || size == 0 ||
                withEntry.Contains(index)) continue;
            if (size > (ulong)(input.Length - elfOffset - headersEnd)) throw Bad();

            // Aligned as its program header asks, when what it asks for is an alignment at all.
            var alignment = (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(programHeader[0x30..]), 0x10000);
            var to = alignment > 1 ? (length + alignment - 1) / alignment * alignment : length;
            BinaryPrimitives.WriteUInt64LittleEndian(programHeader[0x08..], (ulong)to);
            segments.Add(new Segment(input.Length - (long)size, to, (long)size));
            length = to + (long)size;
        }

        // A program header whose data the container does not hold may not point at someone else's.
        var held = Merge(segments.Select(segment => (segment.To, End: segment.To + segment.Length)).Append((0, headersEnd)));
        for (var index = 0; index < programHeaderCount; index++)
        {
            var programHeader = programHeaders.AsSpan(index * ProgramHeaderSize, ProgramHeaderSize);
            var at = BinaryPrimitives.ReadUInt64LittleEndian(programHeader[0x08..]);
            var size = BinaryPrimitives.ReadUInt64LittleEndian(programHeader[0x20..]);
            if (size == 0) continue;
            if (held.Any(range => at >= (ulong)range.Start && at <= (ulong)range.End && size <= (ulong)range.End - at)) continue;
            programHeader.Slice(0x08, 8).Clear();
            programHeader.Slice(0x20, 8).Clear();
        }

        // The sections are not in the container, so the header may not point at them.
        elf.AsSpan(0x28, 8).Clear();
        elf.AsSpan(0x3C, 4).Clear();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        // Written beside the destination and moved over it: a destination that is a link to the dump,
        // left by an earlier conversion, is replaced and never written through.
        var partial = destination + ".part";
        File.Delete(partial);
        try
        {
            using (var output = File.Create(partial))
            {
                output.SetLength(length);
                output.Write(elf);
                output.Position = (long)programHeadersAt;
                output.Write(programHeaders);

                var buffer = new byte[1 << 20];
                foreach (var segment in segments)
                {
                    input.Position = segment.From;
                    output.Position = segment.To;
                    for (var left = segment.Length; left > 0;)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                        if (read == 0) throw Bad();
                        output.Write(buffer, 0, read);
                        left -= read;
                    }
                }
            }
            File.Move(partial, destination, overwrite: true);
        }
        finally
        {
            File.Delete(partial);
        }
    }

    /// <summary>The first bytes of a PS5 or a PS4 container.</summary>
    public static bool IsContainer(ReadOnlySpan<byte> start) =>
        start.Length >= 4 && (start[..4].SequenceEqual<byte>([0x54, 0x14, 0xF5, 0xEE]) ||
                              start[..4].SequenceEqual<byte>([0x4F, 0x15, 0x3D, 0x1D]));

    /// <summary>The ranges as few as they can be: those that touch or overlap become one.</summary>
    private static List<(long Start, long End)> Merge(IEnumerable<(long Start, long End)> ranges)
    {
        var merged = new List<(long Start, long End)>();
        foreach (var range in ranges.OrderBy(range => range.Start))
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
            else
                merged.Add(range);
        }
        return merged;
    }

    private static byte[]? Read(FileStream input, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset > input.Length || count > input.Length - offset) return null;
        var bytes = new byte[count];
        input.Position = offset;
        input.ReadExactly(bytes);
        return bytes;
    }
}
