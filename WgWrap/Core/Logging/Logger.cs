using System.Collections.Concurrent;

namespace WgWrap.Core.Logging;

/// <summary>
/// Simple file-based logging utility with daily rotation and 30-day retention.
/// Now uses asynchronous buffering to minimize I/O contention.
/// </summary>
internal class Logger : IDisposable
{
    private readonly object _rotationLock = new object();
    private readonly string _logDirectory;
    private DateTime _currentLogDate;
    private string _logFilePath;
    private const int RetentionDays = 30;
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>());
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;
    private volatile bool _disposed;

    public Logger()
    {
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        _logDirectory = Path.Combine(appDirectory, "logs");
        _currentLogDate = DateTime.Today;
        _logFilePath = BuildDailyLogFilePath(_currentLogDate);

        try
        {
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }
        catch { }

        TryCleanupOldLogs();

        // Start background writer
        _writerTask = Task.Run(WriterLoopAsync);
    }

    /// <summary>
    /// Log an informational message
    /// </summary>
    public void Info(string message) => Enqueue("INFO", message);

    /// <summary>
    /// Log a warning message
    /// </summary>
    public void Warn(string message) => Enqueue("WARN", message);

    /// <summary>
    /// Log an error message
    /// </summary>
    public void Error(string message) => Enqueue("ERROR", message);

    /// <summary>
    /// Log an error with exception details
    /// </summary>
    public void Error(string message, Exception ex)
    {
        var fullMessage = $"{message}\n  Exception: {ex.GetType().Name}\n  Message: {ex.Message}\n  StackTrace: {ex.StackTrace}";
        Enqueue("ERROR", fullMessage);
    }

    /// <summary>
    /// Log a debug message
    /// </summary>
    public void Debug(string message) => Enqueue("DEBUG", message);

    private void Enqueue(string level, string message)
    {
        if (_disposed) return; // Ignore after disposal
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] [{level}] {message}";
            _queue.Add(logEntry);
        }
        catch { }
    }

    private async Task WriterLoopAsync()
    {
        var batch = new List<string>(capacity: 256);
        var flushInterval = TimeSpan.FromSeconds(1);
        var nextFlush = DateTime.UtcNow + flushInterval;
        try
        {
            while (!_queue.IsCompleted && !_cts.IsCancellationRequested)
            {
                string? entry;
                if (_queue.TryTake(out entry, millisecondsTimeout: 200))
                {
                    batch.Add(entry);
                }

                var now = DateTime.UtcNow;
                if (batch.Count > 0 && (batch.Count >= 256 || now >= nextFlush))
                {
                    await FlushBatchAsync(batch).ConfigureAwait(false);
                    batch.Clear();
                    nextFlush = now + flushInterval;
                }
            }
        }
        catch { }
        finally
        {
            while (_queue.TryTake(out var remaining))
            {
                batch.Add(remaining);
            }
            if (batch.Count > 0)
            {
                await FlushBatchAsync(batch).ConfigureAwait(false);
            }
        }
    }

    private async Task FlushBatchAsync(List<string> batch)
    {
        try
        {
            lock (_rotationLock)
            {
                EnsureDailyRotation();
            }
            await File.AppendAllLinesAsync(_logFilePath, batch).ConfigureAwait(false);
        }
        catch { }
    }

    private string BuildDailyLogFilePath(DateTime date) => Path.Combine(_logDirectory, $"wgwrap-{date:yyyy-MM-dd}.log");

    /// <summary>
    /// Ensure current log file path is for today; rotate if day changed.
    /// </summary>
    private void EnsureDailyRotation()
    {
        var today = DateTime.Today;
        if (today != _currentLogDate)
        {
            _currentLogDate = today;
            _logFilePath = BuildDailyLogFilePath(today);
            // Optionally create empty file to mark new day
            try
            {
                if (!File.Exists(_logFilePath))
                {
                    File.WriteAllText(_logFilePath, string.Empty);
                }
            }
            catch { }
            TryCleanupOldLogs();
        }
    }

    /// <summary>
    /// Deletes log files older than retention period.
    /// </summary>
    private void TryCleanupOldLogs()
    {
        try
        {
            if (!Directory.Exists(_logDirectory)) return;
            var files = Directory.GetFiles(_logDirectory, "wgwrap-*.log");
            var cutoff = DateTime.Today.AddDays(-RetentionDays);
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                // Expect format wgwrap-YYYY-MM-DD.log
                if (name.Length == "wgwrap-YYYY-MM-DD.log".Length)
                {
                    var datePart = name.Substring(7, 10); // YYYY-MM-DD
                    if (DateTime.TryParse(datePart, out var fileDate))
                    {
                        if (fileDate < cutoff)
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Gets the current day's log file path.
    /// </summary>
    public string GetLogFilePath() => _logFilePath;

    /// <summary>
    /// Flushes remaining log entries and stops background writer.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _cts.Cancel();
            _queue.CompleteAdding();
        }
        catch { }
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }
        _cts.Dispose();
    }
}
