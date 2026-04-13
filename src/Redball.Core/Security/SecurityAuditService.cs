using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Redball.Core.Security;

/// <summary>
/// Provides tamper-proof security audit logging using hash-chaining.
/// Each entry contains a SHA-256 hash of (Content + PreviousHash).
/// </summary>
public class SecurityAuditService
{
    private readonly string _logPath;
    private string _lastHash = "INITIAL_SEED";
    private readonly object _lock = new();

    public SecurityAuditService(string logPath)
    {
        _logPath = logPath;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        
        if (File.Exists(_logPath))
        {
            try { _lastHash = GetLastHashFromLog(); } catch { _lastHash = "COMPROMISED_OR_NEW"; }
        }
    }

    public void LogEvent(string component, string eventType, string details, string? userId = null)
    {
        lock (_lock)
        {
            var entry = new AuditEntry
            {
                Timestamp = DateTime.UtcNow,
                Component = component,
                EventType = eventType,
                Details = details,
                UserId = userId ?? Environment.UserName,
                PreviousHash = _lastHash
            };

            var payload = JsonSerializer.Serialize(entry);
            _lastHash = CalculateHash(payload + _lastHash);
            entry.Hash = _lastHash;

            var logLine = JsonSerializer.Serialize(entry) + Environment.NewLine;
            File.AppendAllText(_logPath, logLine);
        }
    }

    public bool VerifyIntegrity(out int failedLine, out string? error)
    {
        lock (_lock)
        {
            failedLine = 0;
            error = null;
            
            if (!File.Exists(_logPath)) return true;

            string expectedPrevHash = "INITIAL_SEED";
            int lineNum = 0;

            foreach (var line in File.ReadLines(_logPath))
            {
                lineNum++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var entry = JsonSerializer.Deserialize<AuditEntry>(line);
                    if (entry == null) throw new Exception("Invalid JSON formatting");

                    // 1. Check if the claimed previous hash matches the anchor
                    if (entry.PreviousHash != expectedPrevHash)
                    {
                        failedLine = lineNum;
                        error = $"Hash chain broken. Expected {expectedPrevHash}, found {entry.PreviousHash}";
                        return false;
                    }

                    // 2. Re-calculate hash of the content (stored hash is excluded from re-calc naturally if we use the object)
                    var currentHash = entry.Hash;
                    entry.Hash = ""; // Clear for recalc
                    var payload = JsonSerializer.Serialize(entry);
                    var recalculated = CalculateHash(payload + expectedPrevHash);

                    if (currentHash != recalculated)
                    {
                        failedLine = lineNum;
                        error = "Content tamper detected. Hash mismatch.";
                        return false;
                    }

                    expectedPrevHash = currentHash;
                }
                catch (Exception ex)
                {
                    failedLine = lineNum;
                    error = $"Parsing error: {ex.Message}";
                    return false;
                }
            }

            return true;
        }
    }

    private string CalculateHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private string GetLastHashFromLog()
    {
        var lastLine = "";
        using (var reader = new StreamReader(_logPath))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line)) lastLine = line;
            }
        }

        if (string.IsNullOrEmpty(lastLine)) return "INITIAL_SEED";
        var entry = JsonSerializer.Deserialize<AuditEntry>(lastLine);
        return entry?.Hash ?? "INITIAL_SEED";
    }

    private class AuditEntry
    {
        public DateTime Timestamp { get; set; }
        public string Component { get; set; } = "";
        public string EventType { get; set; } = "";
        public string Details { get; set; } = "";
        public string UserId { get; set; } = "";
        public string PreviousHash { get; set; } = "";
        public string Hash { get; set; } = "";
    }
}
