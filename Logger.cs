using System.Text;

/// <summary>
/// Scrive ogni messaggio sia sulla console che su un file .log,
/// aggiungendo timestamp a ogni riga.
/// </summary>
class Logger : IDisposable
{
    private readonly StreamWriter _logWriter;

    public Logger(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        // Nome file: export_20250227_143000.log
        var logFileName = $"export_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var logPath = Path.Combine(outputDir, logFileName);

        var fs = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _logWriter = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true // Ogni riga viene scritta subito, anche in caso di crash.
        };

        Console.WriteLine($"Log: {Path.GetFullPath(logPath)}");
    }

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Console.WriteLine(line);
        _logWriter.WriteLine(line);
    }

    public void LogError(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ERRORE] {message}";
        Console.Error.WriteLine(line); // stderr per gli errori
        _logWriter.WriteLine(line);
    }

    public void Dispose() => _logWriter.Dispose();
}