namespace AnyPS5Gui.Core.Tests;

/// <summary>A scratch folder that is deleted when the test ends.</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("anyps5gui-").FullName;

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Writes a file, creating its folders, and returns its path.</summary>
    public string Write(string relative, string content = "x")
    {
        var file = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        return file;
    }

    public string WriteBytes(string relative, params byte[] content)
    {
        var file = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, content);
        return file;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { /* a leftover scratch folder is not worth failing a test */ }
    }
}

public static class Magic
{
    public static readonly byte[] Elf = [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0];
    public static readonly byte[] Self = [0x54, 0x14, 0xF5, 0xEE, 0, 0, 0, 0];
}
