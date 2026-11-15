using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;

namespace AnyPS5Gui;

public enum LogKind { Output, Info, Success, Warning, Error }

public sealed record LogLine(LogKind Kind, string Text)
{
    /// <summary>
    /// Set on a progress line, to what it reports on. The relinker redraws "stage | file |  42%" in
    /// place with carriage returns; here each redraw replaces the line before it instead of adding one.
    /// </summary>
    public string? ProgressKey { get; init; }

    // What a screen reader announces for the line.
    public override string ToString() => Text;
}

/// <summary>
/// Lines of program output for a list on screen. Lines arrive from any thread, in bursts; they are
/// queued and added on the UI thread in batches, so a chatty game does not flood the dispatcher.
/// </summary>
public sealed class LogBuffer
{
    private const int MaxLines = 4000;
    private const int TrimBy = 500;

    private readonly ConcurrentQueue<LogLine> _pending = new();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private int _flushScheduled;

    public ObservableCollection<LogLine> Lines { get; } = [];

    public void Add(LogKind kind, string text) => Enqueue(new LogLine(kind, text));

    private void Enqueue(LogLine line)
    {
        _pending.Enqueue(line);
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
            _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, Flush);
    }

    /// <summary>Program output, coloured by what the line says: AnyPS5 writes notes and errors to the same stream.</summary>
    public void AddOutput(string text) =>
        Enqueue(new LogLine(Classify(text), text) { ProgressKey = ProgressKeyOf(text) });

    public void Clear()
    {
        _pending.Clear();
        Lines.Clear();
    }

    public string GetText() => string.Join(Environment.NewLine, Lines.Select(line => line.Text));

    public static LogKind Classify(string text)
    {
        var line = text.AsSpan().TrimStart();
        if (line.StartsWith("FAIL", StringComparison.Ordinal) ||
            line.StartsWith("FATAL", StringComparison.Ordinal) ||
            line.StartsWith("error", StringComparison.OrdinalIgnoreCase))
            return LogKind.Error;
        if (line.StartsWith("warning", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("note:", StringComparison.OrdinalIgnoreCase))
            return LogKind.Warning;
        return LogKind.Output;
    }

    private static string? ProgressKeyOf(string text)
    {
        if (!text.EndsWith('%')) return null;
        var bar = text.LastIndexOf('|');
        return bar > 0 ? text[..bar] : null;
    }

    private void Flush()
    {
        Interlocked.Exchange(ref _flushScheduled, 0);

        // More waiting than the list can hold: only the newest lines would survive the trim anyway.
        while (_pending.Count > MaxLines) _pending.TryDequeue(out _);

        while (_pending.TryDequeue(out var line))
        {
            if (line.ProgressKey is not null && Lines.Count > 0 && Lines[^1].ProgressKey == line.ProgressKey)
                Lines[^1] = line;
            else
                Lines.Add(line);
        }
        if (Lines.Count > MaxLines)
        {
            var excess = Lines.Count - MaxLines + TrimBy;
            for (var i = 0; i < excess; i++) Lines.RemoveAt(0);
        }
    }
}
