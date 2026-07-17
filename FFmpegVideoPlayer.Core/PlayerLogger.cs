using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FFmpegVideoPlayer.Core;

/// <summary>
/// Lightweight diagnostic logger for player operations. File logging is opt-in so
/// applications are never required to grant write access to their installation directory.
/// </summary>
public sealed class PlayerLogger : IDisposable
{
    private readonly List<LogEntry> _logEntries = new();
    private readonly object _lock = new();
    private readonly string? _logFilePath;
    private bool _disposed;
    private DateTime _lastFlush = DateTime.Now;

    public PlayerLogger(string? logFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(logFilePath))
        {
            _logFilePath = Path.GetFullPath(logFilePath);
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }

        Log("PlayerLogger", "Initialized", new { FileLoggingEnabled = _logFilePath != null });
    }

    public void Log(string component, string operation, object? data = null)
    {
        lock (_lock)
        {
            if (_disposed) return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                TimestampLocal = DateTime.Now,
                Component = component,
                Operation = operation,
                Data = data
            };

            _logEntries.Add(entry);

            // Do not emit data by default: media URLs can contain signed query strings,
            // cookies, or other credentials. Applications can opt into a JSON log file.
            Debug.WriteLine($"[{entry.TimestampLocal:HH:mm:ss.fff}] [{component}] {operation}");
            
            // Auto-flush every 5 seconds to ensure logs are saved even if app crashes
            if (_logFilePath != null && (DateTime.Now - _lastFlush).TotalSeconds > 5)
            {
                Flush();
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_disposed) return;
            
            var clearedCount = _logEntries.Count;
            _logEntries.Clear();

            if (_logFilePath == null)
                return;
            
            // Write empty array to file to clear it
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var json = JsonSerializer.Serialize(new List<LogEntry>(), options);
                File.WriteAllText(_logFilePath, json, Encoding.UTF8);
                _lastFlush = DateTime.Now;
                
                Debug.WriteLine($"[PlayerLogger] Cleared {clearedCount} log entries from {_logFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PlayerLogger] Failed to clear log file: {ex.Message}");
            }
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            if (_disposed || _logFilePath == null || _logEntries.Count == 0) return;

            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(_logEntries, options);
                File.WriteAllText(_logFilePath, json, Encoding.UTF8);
                _lastFlush = DateTime.Now;
                
                Debug.WriteLine($"[PlayerLogger] Flushed {_logEntries.Count} log entries to {_logFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PlayerLogger] Failed to write log file: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            FlushCore();
            _disposed = true;
        }
    }

    private void FlushCore()
    {
        if (_logFilePath == null || _logEntries.Count == 0)
            return;

        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(_logEntries, options);
            File.WriteAllText(_logFilePath, json, Encoding.UTF8);
            _lastFlush = DateTime.Now;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PlayerLogger] Failed to write log file: {ex.Message}");
        }
    }

    private class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public DateTime TimestampLocal { get; set; }
        public string Component { get; set; } = "";
        public string Operation { get; set; } = "";
        public object? Data { get; set; }
    }
}

