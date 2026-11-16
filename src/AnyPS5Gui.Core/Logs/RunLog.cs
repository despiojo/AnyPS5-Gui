using System.Text;
using AnyPS5Gui.Core.Conversion;

namespace AnyPS5Gui.Core.Logs;

/// <summary>
/// The text file of one run: a conversion or a game from start to end. The list on screen keeps only
/// the newest lines; the file keeps all of them, and can be read while the run goes on.
/// </summary>
public sealed class RunLog : IDisposable
{
    private readonly Lock _lock = new();
    private StreamWriter? _writer;
    private string? _progressText;
    private string? _progressKey;

    private RunLog(string path, StreamWriter writer)
    {
        Path = path;
        _writer = writer;
    }

    public string Path { get; }

    /// <summary>
    /// Starts <c>yyyyMMdd-HHmmss-name.log</c> in the directory and deletes the oldest logs there, so that
    /// <paramref name="keep"/> are left counting the new one. Keeping none writes no file: null.
    /// </summary>
    public static RunLog? Start(string directory, string name, int keep, DateTime? now = null)
    {
        Directory.CreateDirectory(directory);
        Prune(directory, Math.Max(0, keep - 1));
        if (keep <= 0) return null;

        var stem = $"{now ?? DateTime.Now:yyyyMMdd-HHmmss}-{OutputNaming.SanitizeFileName(name).Replace(' ', '-')}";
        for (var attempt = 1; ; attempt++)
        {
            var path = System.IO.Path.Combine(directory, attempt == 1 ? stem + ".log" : $"{stem}-{attempt}.log");
            try
            {
                // Shared for reading, so an editor can follow the file, but not for deleting: another run that
                // starts meanwhile must not take away a log whose run has not said its last word.
                var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                return new RunLog(path, new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
            }
            catch (IOException) when (File.Exists(path) && attempt < 100)
            {
                // Another run started in the same second.
            }
        }
    }

    /// <summary>
    /// Deletes all but the newest logs. A file that cannot be deleted now, such as the log of a run that
    /// is still going on, is left for the next time.
    /// </summary>
    public static void Prune(string directory, int keep)
    {
        if (!Directory.Exists(directory)) return;
        // The names start with the time, so their order is the order of the runs.
        var old = Directory.GetFiles(directory, "*.log").OrderDescending(StringComparer.Ordinal).Skip(keep);
        foreach (var file in old)
        {
            try { File.Delete(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Adds a line. Safe from any thread. A line with a progress key that follows one with the same key
    /// replaces it, as it does on screen: the relinker redraws each "stage | file | 42%" many times.
    /// </summary>
    public void Write(string text, string? progressKey = null)
    {
        lock (_lock)
        {
            if (_writer is null) return;
            try
            {
                if (_progressKey is not null && _progressKey != progressKey) FlushProgress();
                if (progressKey is null)
                {
                    _writer.WriteLine(text);
                }
                else
                {
                    _progressText = text;
                    _progressKey = progressKey;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A full or vanished disk: the run goes on without its file.
                Close();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_writer is null) return;
            try { FlushProgress(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Close();
        }
    }

    private void FlushProgress()
    {
        if (_progressText is not null) _writer!.WriteLine(_progressText);
        _progressText = _progressKey = null;
    }

    private void Close()
    {
        try { _writer?.Dispose(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        _writer = null;
    }
}
