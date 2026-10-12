using System;
using System.IO;

namespace ConfigManager.Services;

public interface ILoggerService
{
    void LogError(string message, Exception? ex = null);
    void LogInfo(string message);
    void LogWarning(string message);
    string LogDirectory { get; }
    string CurrentLogFilePath { get; }
}

public class LoggerService : ILoggerService
{
    private static readonly object _lock = new();
    private readonly string _logDirectory;

    public string LogDirectory => _logDirectory;

    public string CurrentLogFilePath
    {
        get
        {
            var fileName = $"error_{DateTime.Now:yyyyMMdd}.log";
            return Path.Combine(_logDirectory, fileName);
        }
    }

    public LoggerService()
    {
        // Place logs folder in the application's base directory
        _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        try
        {
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }
        catch
        {
            // Fallback to local appdata if base directory is write-protected
            _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConfigManager", "logs");
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }
    }

    public void LogError(string message, Exception? ex = null)
    {
        WriteLog("ERROR", message, ex);
    }

    public void LogWarning(string message)
    {
        WriteLog("WARN", message, null);
    }

    public void LogInfo(string message)
    {
        WriteLog("INFO", message, null);
    }

    private void WriteLog(string level, string message, Exception? ex)
    {
        try
        {
            lock (_lock)
            {
                var filePath = CurrentLogFilePath;
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using var writer = new StreamWriter(filePath, append: true, System.Text.Encoding.UTF8);
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var threadId = Environment.CurrentManagedThreadId;

                writer.WriteLine($"[{timestamp}] [{level}] [Thread:{threadId}] {message}");

                if (ex != null)
                {
                    writer.WriteLine($"Exception Type: {ex.GetType().FullName}");
                    writer.WriteLine($"Message: {ex.Message}");
                    writer.WriteLine($"StackTrace:\n{ex.StackTrace}");

                    var inner = ex.InnerException;
                    int depth = 1;
                    while (inner != null && depth <= 5)
                    {
                        writer.WriteLine($"  ---> Inner Exception [{depth}]: {inner.GetType().FullName}: {inner.Message}");
                        writer.WriteLine($"       StackTrace:\n{inner.StackTrace}");
                        inner = inner.InnerException;
                        depth++;
                    }
                }

                writer.WriteLine(new string('-', 80));
            }
        }
        catch (Exception writeEx)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write to log file: {writeEx.Message}");
        }
    }
}
