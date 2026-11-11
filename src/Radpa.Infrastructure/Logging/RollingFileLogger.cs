using System.Globalization;
using System.Text;
using Radpa.Application.Abstractions;

namespace Radpa.Infrastructure.Logging;

public sealed class RollingFileLogger : IAppLogger, IDisposable
{
    private const long MaxFileSizeBytes = 2 * 1024 * 1024;
    private const int MaxArchivedFiles = 3;

    private readonly string _logFilePath;
    private readonly Lock _sync = new();
    private StreamWriter? _writer;

    public RollingFileLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        _logFilePath = Path.Combine(logDirectory, "radpa.log");
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warning(string message) => Write("WARN", message, null);

    public void LogError(string message, Exception? exception = null) => Write("ERROR", message, exception);

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(string level, string message, Exception? ex)
    {
        var ts = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var line = ex is null
            ? $"[{ts}] [{level}] {message}"
            : $"[{ts}] [{level}] {message} | {ex.GetType().Name}: {ex.Message}";

        lock (_sync)
        {
            try
            {
                RotateIfNeededLocked();
                _writer ??= new StreamWriter(new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
                {
                    AutoFlush = true,
                };
                _writer.WriteLine(line);
            }
            catch
            {
            }
        }
    }

    private void RotateIfNeededLocked()
    {
        if (!File.Exists(_logFilePath))
        {
            return;
        }

        var info = new FileInfo(_logFilePath);
        if (info.Length < MaxFileSizeBytes)
        {
            return;
        }

        _writer?.Dispose();
        _writer = null;

        for (var i = MaxArchivedFiles - 1; i >= 1; i--)
        {
            var src = $"{_logFilePath}.{i}";
            var dst = $"{_logFilePath}.{i + 1}";
            if (File.Exists(src))
            {
                File.Move(src, dst, overwrite: true);
            }
        }

        File.Move(_logFilePath, $"{_logFilePath}.1", overwrite: true);
    }
}
