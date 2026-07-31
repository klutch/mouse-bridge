using System.Collections.Concurrent;
using System.Text;

namespace MouseBridge;

/// <summary>
/// Append-only log with the file I/O kept off the hook callback.
/// </summary>
/// <remarks>
/// The low-level hook must return promptly — Windows silently drops hooks that
/// exceed LowLevelHooksTimeout — so the callback only enqueues, and a timer
/// thread does the writing.
/// </remarks>
internal sealed class Logger : IDisposable
{
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly System.Threading.Timer _flusher;
    private readonly string _path;

    /// <summary>When true, every mouse move that leaves the desktop is recorded.</summary>
    public bool Verbose { get; set; }

    public string Path => _path;
    public string Folder => System.IO.Path.GetDirectoryName(_path)!;

    public Logger(bool verbose)
    {
        Verbose = verbose;

        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MouseBridge");
        Directory.CreateDirectory(dir);
        _path = System.IO.Path.Combine(dir, "mousebridge.log");

        _flusher = new System.Threading.Timer(_ => Flush(), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void Write(string message) =>
        _pending.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {message}");

    /// <summary>
    /// Diagnostic hook-vs-cursor comparison. This is what tells us whether the
    /// hook really reports the pre-clamp position, so it logs the interesting
    /// case only: a target point that no monitor contains.
    /// </summary>
    public void Trace(POINT desired, POINT current, Topology topology)
    {
        if (!Verbose || topology.Contains(desired)) return;
        Write($"offdesk hook={desired} cursor={current} delta=({desired.X - current.X},{desired.Y - current.Y})");
    }

    public void Flush()
    {
        if (_pending.IsEmpty) return;

        var buffer = new StringBuilder();
        while (_pending.TryDequeue(out var line)) buffer.AppendLine(line);

        try
        {
            File.AppendAllText(_path, buffer.ToString(), Encoding.UTF8);
        }
        catch (IOException)
        {
            // A transient lock on the log file is not worth taking the tool down for.
        }
    }

    public void Dispose()
    {
        _flusher.Dispose();
        Flush();
    }
}
