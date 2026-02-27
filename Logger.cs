using System.Text;

/// <summary>
/// A dual-output logger that writes every message to both the console and a .log file,
/// prefixing each line with a timestamp so the exact sequence of events is always clear.
/// Implements <see cref="IDisposable"/> so the underlying file handle is released correctly
/// at the end of a <c>using</c> block.
/// </summary>
internal class Logger : IDisposable
{
    // StreamWriter dedicated to the .log file.
    // Declared readonly because it is assigned exactly once in the constructor
    // and must never be replaced by a different writer instance afterwards.
    private readonly StreamWriter _logWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="Logger"/> class.
    /// Creates the output directory if it does not already exist, then opens a new
    /// timestamped .log file inside that directory.
    /// </summary>
    /// <param name="outputDir">
    /// Directory where the .log file will be created.
    /// The directory (and any missing parent directories) is created automatically.
    /// </param>
    public Logger(string outputDir)
    {
        // Create the output directory recursively if it does not exist yet.
        // No-op if the directory is already present, so this is always safe to call.
        Directory.CreateDirectory(outputDir);

        // Embed the start timestamp in the file name so that each program run produces
        // a distinct file and never overwrites logs from previous executions.
        // Example: export_20250227_143000.log
        var logFileName = $"export_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var logPath = Path.Combine(outputDir, logFileName);

        // Open the file with exclusive write access (FileAccess.Write) while still
        // allowing other processes to open it for reading at the same time (FileShare.Read).
        // This makes it possible to monitor the log in real time with a text editor
        // or with "tail -f" on Linux without interfering with the ongoing write.
        var fs = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read);

        // Wrap the raw FileStream in a StreamWriter using UTF-8 without a BOM,
        // which ensures compatibility with most log viewers and Unix tools.
        _logWriter = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            // AutoFlush = true forces the internal buffer to be flushed to disk after
            // every WriteLine call. Without this, a sudden crash could cause the last
            // few log lines to be lost because they were still sitting in memory.
            AutoFlush = true
        };

        // Print the absolute path of the newly created log file so the user knows
        // exactly where to find it, regardless of the current working directory.
        Console.WriteLine($"Log: {Path.GetFullPath(logPath)}");
    }

    /// <summary>
    /// Writes an informational message to both stdout (console) and the log file,
    /// prefixed with the current timestamp.
    /// </summary>
    /// <param name="message">The message text to record.</param>
    public void Log(string message)
    {
        // Build the formatted line once and reuse the same string for both destinations.
        // This guarantees that the console and the file always contain identical text,
        // avoiding any subtle divergence from two separate string-format calls.
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

        Console.WriteLine(line);    // Show to the user in real time via stdout.
        _logWriter.WriteLine(line); // Persist to disk for later review.
    }

    /// <summary>
    /// Writes an error message to both stderr (console) and the log file,
    /// adding an <c>[ERRORE]</c> tag to make errors easy to spot when scanning the log.
    /// </summary>
    /// <param name="message">The error text to record.</param>
    public void LogError(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ERRORE] {message}";

        // Write errors to stderr (Console.Error) rather than stdout (Console.WriteLine).
        // Keeping the two streams separate lets calling scripts and CI pipelines
        // redirect or filter normal output and errors independently — for example,
        // piping stdout to a file while still seeing errors on the terminal.
        Console.Error.WriteLine(line);

        // Also write the error to the log file so the log is a complete, self-contained
        // record of the run. Without this, reconstructing the full event sequence would
        // require merging the stdout and stderr streams externally.
        _logWriter.WriteLine(line);
    }

    /// <summary>
    /// Releases the underlying file handle by disposing the <see cref="StreamWriter"/>,
    /// which in turn flushes any remaining buffered data and closes the .log file.
    /// Called automatically at the end of a <c>using</c> block.
    /// </summary>
    public void Dispose() => _logWriter.Dispose();
}