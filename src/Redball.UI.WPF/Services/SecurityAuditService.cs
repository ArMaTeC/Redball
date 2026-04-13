using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Redball.Core.Security;

namespace Redball.UI.Services;

/// <summary>
/// Provides tamper-proof security auditing. Logs critical events with hash-chaining
/// to ensure that if a log entry is modified or deleted, the chain breaks.
/// </summary>
public class SecurityAuditService
{
    private static readonly Lazy<SecurityAuditService> _instance = new(() => new SecurityAuditService());
    public static SecurityAuditService Instance => _instance.Value;

    private readonly string _auditLogPath;
    private string _lastHash = "INITIAL_SEED";
    private readonly object _lock = new();

    private SecurityAuditService()
    {
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Redball", "Logs");
        Directory.CreateDirectory(logDir);
        _auditLogPath = Path.Combine(logDir, "security.audit.log");
        
        // Initialize chain from existing log if possible
        TryInitializeChain();
    }

    private void TryInitializeChain()
    {
        try
        {
            if (File.Exists(_auditLogPath))
            {
                var lines = File.ReadAllLines(_auditLogPath);
                if (lines.Length > 0)
                {
                    var lastLine = lines[^1];
                    var parts = lastLine.Split('|');
                    if (parts.Length > 0)
                    {
                        _lastHash = parts[0]; // The first part is always the hash of that entry
                    }
                }
            }
        }
        catch { /* Fallback to seed */ }
    }

    /// <summary>
    /// Logs a security event with a hash signature.
    /// </summary>
    public void LogEvent(string category, string message)
    {
        lock (_lock)
        {
            try
            {
                var timestamp = DateTime.UtcNow.ToString("O");
                var machine = Environment.MachineName;
                var user = Environment.UserName;
                
                // Data to be hashed: LastHash + NewEventInfo
                var rawData = $"{_lastHash}|{timestamp}|{machine}|{user}|{category}|{message}";
                var newHash = ComputeHash(rawData);
                
                var entry = $"{newHash}|{rawData}";
                File.AppendAllLines(_auditLogPath, new[] { entry });
                
                _lastHash = newHash;
                Logger.Info("SecurityAudit", $"AUDIT LOGGED: {category} - {message}");
            }
            catch (Exception ex)
            {
                Logger.Error("SecurityAudit", "CRITICAL: Failed to write security audit log", ex);
            }
        }
    }

    private string ComputeHash(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    public bool VerifyLogIntegrity()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_auditLogPath)) return true;
                
                var lines = File.ReadAllLines(_auditLogPath);
                var currentChainHash = "INITIAL_SEED";
                
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length < 7) return false; // Tampered or corrupt
                    
                    var storedHash = parts[0];
                    var rawDataBuilder = new StringBuilder();
                    rawDataBuilder.Append(currentChainHash);
                    for (int i = 1; i < parts.Length; i++)
                    {
                        rawDataBuilder.Append("|" + parts[i]);
                    }
                    
                    var computedHash = ComputeHash(rawDataBuilder.ToString());
                    if (computedHash != storedHash) return false; // Mismatch!
                    
                    currentChainHash = storedHash;
                }
                
                return true;
            }
            catch { return false; }
        }
    }
}
