using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

// Alias to disambiguate from Logger.Debug method
using SysDebug = System.Diagnostics.Debug;

namespace Redball.UI.Services;

/// <summary>
/// Comprehensive logging service for Redball WPF application.
/// Thread-safe file-based logging with rotation and crash dump support.
/// </summary>
public static class Logger
{
    private static readonly object _lock = new();
    private static string _logPath = "";
    private static volatile bool _initialized = false;
    private static int _logLevel = 0; // 0=Verbose, 1=Debug, 2=Info, 3=Warning, 4=Error, 5=Fatal
    private static readonly char[] NewLineChars = { '\r', '\n' };

    // Async log channel: callers enqueue formatted strings, background task writes to disk
    private static Channel<string>? _channel;
    private static Task? _writerTask;
    private static readonly CancellationTokenSource _cts = new();

    // Windows Event Log integration for service-tier logging
    private static EventLog? _eventLog;
    private static bool _eventLogEnabled = false;

    public static string LogPath => _logPath;

    public static int CurrentLogLevel => _logLevel;

    /// <summary>
    /// Initialize the logger with the specified log file path.
    /// </summary>
    public static void Initialize(string? logPath = null)
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;

            _logPath = logPath ?? Path.Combine(AppContext.BaseDirectory, "logs", "Redball.UI.log");
            
            try
            {
                // Ensure directory exists
                var dir = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Write startup header — single write instead of 8 separate File.AppendAllText calls
                var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var header = new StringBuilder(512);
                header.AppendLine();
                header.AppendLine($"[{ts}] ========== Redball WPF Started ==========");
                header.AppendLine($"[{ts}] Version: {GetAppVersion()}");
                header.AppendLine($"[{ts}] Process ID: {Environment.ProcessId}");
                header.AppendLine($"[{ts}] Base Directory: {AppContext.BaseDirectory}");
                header.AppendLine($"[{ts}] Current Directory: {Environment.CurrentDirectory}");
                header.AppendLine($"[{ts}] OS: {Environment.OSVersion}");
                header.AppendLine($"[{ts}] .NET Version: {Environment.Version}");
                header.AppendLine($"[{ts}] Command Line: {Environment.CommandLine}");
                File.AppendAllText(_logPath, header.ToString());

                // Start the async background writer
                _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
                _writerTask = Task.Run(() => BackgroundWriterAsync(_cts.Token));

                // Initialize Windows Event Log for service-tier logging
                InitializeEventLog();

                _initialized = true;
            }
            catch (Exception ex)
            {
                // Fallback to temp if we can't write to the requested location
                _logPath = Path.Combine(Path.GetTempPath(), "Redball.UI.log");
                try
                {
                    File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FALLBACK: Logger initialization failed: {ex.Message}{Environment.NewLine}");
                    _initialized = true;
                }
                catch { /* Silent fail - we tried */ }
            }
        }
    }

    /// <summary>
    /// Initialize Windows Event Log integration for service-tier logging.
    /// </summary>
    private static void InitializeEventLog()
    {
        try
        {
            const string sourceName = "Redball";
            const string logName = "Application";

            // Create event source if it doesn't exist (requires admin rights)
            if (!EventLog.SourceExists(sourceName))
            {
                EventLog.CreateEventSource(sourceName, logName);
            }

            _eventLog = new EventLog(logName)
            {
                Source = sourceName
            };
            _eventLogEnabled = true;
        }
        catch (Exception ex)
        {
            // Event Log access may require elevated privileges
            _eventLogEnabled = false;
            System.Diagnostics.Debug.WriteLine($"[Logger] Event Log initialization failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Write a log entry to Windows Event Log (for Error and Fatal levels).
    /// </summary>
    private static void WriteToEventLog(string level, string component, string message)
    {
        if (!_eventLogEnabled || _eventLog == null) return;

        try
        {
            var eventType = level switch
            {
                "ERR" => EventLogEntryType.Error,
                "FTL" => EventLogEntryType.Error,
                "WRN" => EventLogEntryType.Warning,
                _ => EventLogEntryType.Information
            };

            // Only write Error, Fatal, and Warning to Event Log to avoid spam
            if (level is "ERR" or "FTL" or "WRN")
            {
                var eventMessage = $"[{component}] {message}";
                // Truncate if too long (Event Log has ~31KB limit)
                if (eventMessage.Length > 30000)
                {
                    eventMessage = eventMessage[..30000] + "... (truncated)";
                }
                _eventLog.WriteEntry(eventMessage, eventType);
            }
        }
        catch
        {
            // Silently fail - Event Log is best-effort
            _eventLogEnabled = false;
        }
    }

    /// <summary>
    /// Set minimum log level (0=Verbose, 1=Debug, 2=Info, 3=Warning, 4=Error, 5=Fatal)
    /// </summary>
    public static void SetLogLevel(int level)
    {
        _logLevel = Math.Clamp(level, 0, 5);
    }

    public static void ApplyConfig(RedballConfig config)
    {
        if (config == null)
        {
            return;
        }

        SetLogLevel(config.VerboseLogging ? 0 : 2);
        RotateLog(config.MaxLogSizeMB * 1024L * 1024L);
        Info("Logger", $"Logger configuration applied: Level={_logLevel}, MaxSizeMB={config.MaxLogSizeMB}");
    }

    public static string GetLogDirectory()
    {
        return Path.GetDirectoryName(_logPath) ?? AppContext.BaseDirectory;
    }

    public static string ExportDiagnostics(string destinationPath)
    {
        var diagnostics = new StringBuilder();
        diagnostics.AppendLine("Redball Diagnostics");
        diagnostics.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        diagnostics.AppendLine($"Version: {GetAppVersion()}");
        diagnostics.AppendLine($"LogPath: {_logPath}");
        diagnostics.AppendLine($"LogLevel: {_logLevel}");
        diagnostics.AppendLine();

        try
        {
            var config = ConfigService.Instance.Config;
            diagnostics.AppendLine("Configuration:");
            diagnostics.AppendLine(JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            diagnostics.AppendLine();
        }
        catch (Exception ex)
        {
            diagnostics.AppendLine($"Configuration export failed: {ex.Message}");
            diagnostics.AppendLine();
        }

        try
        {
            if (File.Exists(_logPath))
            {
                diagnostics.AppendLine("Recent Log Output:");
                // Stream-read only the tail instead of loading entire file into memory
                var tailLines = new LinkedList<string>();
                const int maxTailLines = 200;
                using (var reader = new StreamReader(_logPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
                    new FileStreamOptions { Access = FileAccess.Read, Share = FileShare.ReadWrite }))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        tailLines.AddLast(line);
                        if (tailLines.Count > maxTailLines)
                            tailLines.RemoveFirst();
                    }
                }
                foreach (var line in tailLines)
                {
                    diagnostics.AppendLine(line);
                }
            }
        }
        catch (Exception ex)
        {
            diagnostics.AppendLine($"Log export failed: {ex.Message}");
        }

        File.WriteAllText(destinationPath, diagnostics.ToString());
        return destinationPath;
    }

    /// <summary>
    /// Log a verbose message (level 0)
    /// </summary>
    public static void Verbose(string component, string message)
    {
        if (_logLevel <= 0) Write("VRB", component, message);
    }

    /// <summary>
    /// Log a debug message (level 1)
    /// </summary>
    public static void Debug(string component, string message)
    {
        if (_logLevel <= 1) Write("DBG", component, message);
    }

    /// <summary>
    /// Log an info message (level 2)
    /// </summary>
    public static void Info(string component, string message)
    {
        if (_logLevel <= 2) Write("INF", component, message);
    }

    /// <summary>
    /// Log a warning message (level 3)
    /// </summary>
    public static void Warning(string component, string message)
    {
        if (_logLevel <= 3) Write("WRN", component, message);
    }

    /// <summary>
    /// Log an error message (level 4)
    /// </summary>
    public static void Error(string component, string message)
    {
        if (_logLevel <= 4) Write("ERR", component, message);
    }

    /// <summary>
    /// Log an error with exception details
    /// </summary>
    public static void Error(string component, string message, Exception ex)
    {
        if (_logLevel <= 4)
        {
            var sb = new StringBuilder();
            sb.AppendLine(message);
            sb.AppendLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine($"  Stack: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                sb.AppendLine($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            Write("ERR", component, sb.ToString().TrimEnd());
        }
    }

    /// <summary>
    /// Log a fatal error message (level 5)
    /// </summary>
    public static void Fatal(string component, string message)
    {
        Write("FTL", component, message);
    }

    /// <summary>
    /// Log a fatal error with exception details
    /// </summary>
    public static void Fatal(string component, string message, Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine(message);
        sb.AppendLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
        sb.AppendLine($"  Stack: {ex.StackTrace}");
        if (ex.InnerException != null)
        {
            sb.AppendLine($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            if (ex.InnerException.StackTrace != null)
            {
                sb.AppendLine($"  Inner Stack: {ex.InnerException.StackTrace}");
            }
        }
        
        // Try to get loader exceptions for reflection type load exceptions
        if (ex is System.Reflection.ReflectionTypeLoadException rtle)
        {
            foreach (var loaderEx in rtle.LoaderExceptions ?? Array.Empty<Exception>())
            {
                if (loaderEx != null)
                {
                    sb.AppendLine($"  Loader: {loaderEx.GetType().Name}: {loaderEx.Message}");
                }
            }
        }
        
        Write("FTL", component, sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Write a crash dump with full exception details and application state.
    /// </summary>
    public static void WriteCrashDump(Exception ex, string? context = null)
    {
        try
        {
            var crashPath = Path.Combine(
                Path.GetDirectoryName(_logPath) ?? AppContext.BaseDirectory,
                $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            var sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine("REDBALL WPF CRASH DUMP");
            sb.AppendLine("========================================");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Version: {GetAppVersion()}");
            sb.AppendLine($"Process ID: {Environment.ProcessId}");
            sb.AppendLine($"Context: {context ?? "Unknown"}");
            sb.AppendLine();
            sb.AppendLine("--- EXCEPTION ---");
            sb.AppendLine($"Type: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"Source: {ex.Source}");
            sb.AppendLine($"TargetSite: {ex.TargetSite}");
            sb.AppendLine();
            sb.AppendLine("--- STACK TRACE ---");
            sb.AppendLine(ex.StackTrace);
            sb.AppendLine();
            
            if (ex.InnerException != null)
            {
                sb.AppendLine("--- INNER EXCEPTION ---");
                sb.AppendLine($"Type: {ex.InnerException.GetType().FullName}");
                sb.AppendLine($"Message: {ex.InnerException.Message}");
                sb.AppendLine($"Stack: {ex.InnerException.StackTrace}");
                sb.AppendLine();
            }

            if (ex is System.Reflection.ReflectionTypeLoadException rtle)
            {
                sb.AppendLine("--- TYPE LOAD EXCEPTIONS ---");
                foreach (var loaderEx in rtle.LoaderExceptions ?? Array.Empty<Exception>())
                {
                    if (loaderEx != null)
                    {
                        sb.AppendLine($"Loader: {loaderEx.GetType().Name}: {loaderEx.Message}");
                    }
                }
                sb.AppendLine();
            }

            if (ex is System.IO.FileNotFoundException fnf)
            {
                sb.AppendLine("--- FILE NOT FOUND DETAILS ---");
                sb.AppendLine($"FileName: {fnf.FileName}");
                sb.AppendLine($"FusionLog: {fnf.FusionLog}");
                sb.AppendLine();
            }

            sb.AppendLine("--- SYSTEM INFO ---");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"64-bit: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"Process 64-bit: {Environment.Is64BitProcess}");
            sb.AppendLine($".NET Version: {Environment.Version}");
            sb.AppendLine($"Processor Count: {Environment.ProcessorCount}");
            sb.AppendLine($"Working Set: {Environment.WorkingSet / 1024 / 1024} MB");
            sb.AppendLine();

            sb.AppendLine("--- LOADED ASSEMBLIES ---");
#pragma warning disable IL3000 // Assembly.Location returns empty for single-file apps, handled intentionally
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var loc = asm.IsDynamic ? "[Dynamic]" : 
                        (string.IsNullOrEmpty(asm.Location) ? "[Bundled/Single-file]" : asm.Location);
                    sb.AppendLine($"  {asm.FullName} -> {loc}");
                }
                catch (Exception asmEx)
                {
                    sb.AppendLine($"  {asm.FullName} -> [Error: {asmEx.Message}]");
                }
            }
#pragma warning restore IL3000
            sb.AppendLine();

            sb.AppendLine("========================================");
            sb.AppendLine("END CRASH DUMP");
            sb.AppendLine("========================================");

            File.WriteAllText(crashPath, sb.ToString());
            
            // Also write to main log
            Fatal("CrashDumper", $"Crash dump written to: {crashPath}");
        }
        catch (Exception dumpEx)
        {
            Fatal("CrashDumper", $"Failed to write crash dump: {dumpEx.Message}");
        }
    }

    /// <summary>
    /// Log current memory usage and working set.
    /// </summary>
    public static void LogMemoryStats(string component)
    {
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        Info(component, $"Memory: WorkingSet={proc.WorkingSet64 / 1024 / 1024}MB, Private={proc.PrivateMemorySize64 / 1024 / 1024}MB, GC={GC.GetTotalMemory(false) / 1024 / 1024}MB");
    }

    /// <summary>
    /// Rotate log file if it exceeds max size (10MB default).
    /// </summary>
    public static void RotateLog(long maxSizeBytes = 10 * 1024 * 1024)
    {
        try
        {
            if (!File.Exists(_logPath)) return;
            var info = new FileInfo(_logPath);
            if (info.Length < maxSizeBytes) return;

            var backup = _logPath + ".old";
            try
            {
                File.Delete(backup); // Safe to call even if file doesn't exist
            }
            catch (Exception ex)
            {
                SysDebug.WriteLine($"[Logger] Failed to delete old backup: {ex.Message}");
            }
            File.Move(_logPath, backup);
            Info("Logger", $"Log rotated (was {info.Length / 1024 / 1024}MB)");
        }
        catch (Exception ex)
        {
            Error("Logger", "Failed to rotate log", ex);
        }
    }

    private static string GetAppVersion()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version?.ToString() ?? "Unknown";
        }
        catch (Exception ex)
        {
            SysDebug.WriteLine($"[Logger] Failed to get app version: {ex.Message}");
            return "Unknown";
        }
    }

    private static readonly string _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string _userName = Environment.UserName;

    private static string RedactPii(string message)
    {
        // Redact Windows user profile paths (e.g. C:\Users\JohnDoe\... → C:\Users\<user>\...)
        if (!string.IsNullOrEmpty(_userProfile))
        {
            message = message.Replace(_userProfile, Path.Combine(Path.GetDirectoryName(_userProfile)!, "<user>"));
        }

        // Redact bare username occurrences in paths
        if (_userName.Length >= 3)
        {
            message = message.Replace($"\\{_userName}\\", "\\<user>\\");
        }

        return message;
    }

    private static void Write(string level, string component, string message)
    {
        if (!_initialized) Initialize();

        var redacted = RedactPii(message);

        // Format synchronously on the caller's thread (captures accurate timestamp & thread ID)
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var threadId = Thread.CurrentThread.ManagedThreadId;
        var lines = redacted.Split(NewLineChars, StringSplitOptions.RemoveEmptyEntries);

        var sb = new StringBuilder(lines.Length * 80);
        foreach (var line in lines)
        {
            sb.Append('[').Append(timestamp).Append("] [").Append(level)
              .Append("] [").Append(threadId.ToString("D3")).Append("] [")
              .Append(component).Append("] ").AppendLine(line);
        }
        var formatted = sb.ToString();

        // Also write to Windows Event Log for service-tier visibility
        WriteToEventLog(level, component, redacted);

        // Enqueue to async channel if available; fall back to synchronous write
        if (_channel != null && _channel.Writer.TryWrite(formatted))
        {
            return;
        }

        // Fallback: synchronous write (channel not ready, or backpressure)
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logPath, formatted);
            }
            catch (Exception ex)
            {
                SysDebug.WriteLine($"[Logger] Failed to write log entry: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Background task that drains the log channel and writes to disk in batches.
    /// </summary>
    private static async Task BackgroundWriterAsync(CancellationToken ct)
    {
        var batch = new List<string>(32);
        try
        {
            while (await _channel!.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                batch.Clear();
                while (_channel.Reader.TryRead(out var entry))
                {
                    batch.Add(entry);
                    if (batch.Count >= 64) break; // flush in chunks
                }

                if (batch.Count > 0)
                {
                    var combined = string.Concat(batch);
                    lock (_lock)
                    {
                        try
                        {
                            File.AppendAllText(_logPath, combined);
                        }
                        catch (Exception ex)
                        {
                            SysDebug.WriteLine($"[Logger] Background writer failed to write: {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown - no action needed
        }
    }

    /// <summary>
    /// Flushes all pending log entries to disk synchronously. Call before app exit.
    /// </summary>
    public static void Flush()
    {
        if (_channel == null) return;

        // Drain remaining items from the channel
        var remaining = new List<string>();
        while (_channel.Reader.TryRead(out var entry))
        {
            remaining.Add(entry);
        }

        if (remaining.Count > 0)
        {
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logPath, string.Concat(remaining));
                }
                catch (Exception ex)
                {
                    SysDebug.WriteLine($"[Logger] Flush failed to write remaining entries: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Shuts down the background writer gracefully. Call once during app shutdown.
    /// </summary>
    public static void Shutdown()
    {
        _channel?.Writer.TryComplete();
        try { _writerTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (Exception ex)
        {
            SysDebug.WriteLine($"[Logger] Warning: Writer task did not complete gracefully: {ex.Message}");
        }
        Flush();
    }
}
