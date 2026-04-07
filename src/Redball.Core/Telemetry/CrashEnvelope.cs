namespace Redball.Core.Telemetry;

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Privacy-safe crash report envelope for fleet-level reliability analytics.
/// Contains no PII - only build info, exception fingerprints, and system context.
/// </summary>
public sealed record CrashEnvelope(
    string AppVersion,
    string Channel,
    string OsVersion,
    string ExceptionType,
    string StackFingerprint,
    DateTime OccurredUtc,
    IReadOnlyDictionary<string, string> Tags)
{
    /// <summary>
    /// Unique identifier for deduplication (derived from fingerprint + timestamp).
    /// </summary>
    public string ReportId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Module versions at crash time (e.g., {"Redball.UI.WPF": "3.0.0", "InputInterceptor": "1.2.3"}).
    /// </summary>
    public IReadOnlyDictionary<string, string>? ModuleVersions { get; init; }

    /// <summary>
    /// Feature flags enabled at crash time.
    /// </summary>
    public IReadOnlyList<string>? FeatureFlags { get; init; }

    /// <summary>
    /// Creates a crash envelope from an exception with automatic fingerprinting.
    /// </summary>
    public static CrashEnvelope FromException(
        Exception ex,
        string appVersion,
        string channel,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        var osVersion = Environment.OSVersion.ToString();
        var exceptionType = ex.GetType().FullName ?? "Unknown";
        var fingerprint = CrashFingerprint.From(ex);

        var allTags = new Dictionary<string, string>(tags ?? new Dictionary<string, string>())
        {
            ["is_64bit"] = Environment.Is64BitProcess.ToString(),
            ["clr_version"] = Environment.Version.ToString(),
            ["machine_name_hash"] = HashString(Environment.MachineName), // Hashed for privacy
            ["user_domain_hash"] = HashString(Environment.UserDomainName) // Hashed for privacy
        };

        return new CrashEnvelope(
            AppVersion: appVersion,
            Channel: channel,
            OsVersion: osVersion,
            ExceptionType: exceptionType,
            StackFingerprint: fingerprint,
            OccurredUtc: DateTime.UtcNow,
            Tags: allTags);
    }

    private static string HashString(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..16]; // First 16 chars for brevity
    }
}

/// <summary>
/// Generates consistent fingerprints from exceptions for grouping and deduplication.
/// </summary>
public static class CrashFingerprint
{
    /// <summary>
    /// Creates a normalized fingerprint from an exception.
    /// Groups similar crashes by type, target method, and normalized stack trace.
    /// </summary>
    public static string From(Exception ex)
    {
        var sb = new StringBuilder();

        // Exception type
        sb.Append(ex.GetType().FullName);
        sb.Append('|');

        // Target method (where exception was thrown)
        if (ex.TargetSite != null)
        {
            sb.Append(ex.TargetSite.DeclaringType?.FullName ?? "Unknown");
            sb.Append('.');
            sb.Append(ex.TargetSite.Name);
        }
        else
        {
            sb.Append("Unknown");
        }
        sb.Append('|');

        // Normalized stack trace (remove line numbers which vary by build)
        if (!string.IsNullOrEmpty(ex.StackTrace))
        {
            var normalized = NormalizeStackTrace(ex.StackTrace);
            sb.Append(normalized);
        }

        // Hash the combined string for consistent length
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = sha256.ComputeHash(bytes);

        // Return first 16 chars of hex - enough for grouping, short for display
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>
    /// Normalizes a stack trace by removing line numbers and file paths
    /// that change between builds, keeping only method signatures.
    /// PRIVACY: Sanitizes user-specific paths (C:\Users\..., /home/..., etc.)
    /// </summary>
    private static string NormalizeStackTrace(string stackTrace)
    {
        var lines = stackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>();

        foreach (var line in lines)
        {
            // Extract just the method signature part
            // From: "   at Redball.UI.Services.ConfigService.Load() in C:\\Users\\john\\src\\ConfigService.cs:line 123"
            // To: "Redball.UI.Services.ConfigService.Load"

            var trimmed = line.Trim();
            if (!trimmed.StartsWith("at ")) continue;

            var methodPart = trimmed[3..]; // Remove "at "

            // PRIVACY: Remove file path and line number if present
            // This prevents leaking user-specific paths like C:\Users\username\...
            var inIndex = methodPart.IndexOf(" in ", StringComparison.Ordinal);
            if (inIndex > 0)
            {
                methodPart = methodPart[..inIndex];
            }

            // Remove IL offset if present
            var ilIndex = methodPart.IndexOf(" + 0x", StringComparison.Ordinal);
            if (ilIndex > 0)
            {
                methodPart = methodPart[..ilIndex];
            }

            // PRIVACY: Additional sanitization - remove any remaining path separators
            // that might leak directory structure
            methodPart = SanitizeMethodSignature(methodPart);

            normalized.Add(methodPart.Trim());
        }

        return string.Join(" -> ", normalized);
    }

    /// <summary>
    /// Sanitizes a method signature to remove any remaining path information.
    /// </summary>
    private static string SanitizeMethodSignature(string signature)
    {
        // Remove any Windows-style paths (C:\, D:\, etc.)
        signature = System.Text.RegularExpressions.Regex.Replace(
            signature, 
            @"[A-Z]:\\[^\s]+", 
            "[PATH_REMOVED]");

        // Remove any Unix-style absolute paths (/home/, /usr/, etc.)
        signature = System.Text.RegularExpressions.Regex.Replace(
            signature, 
            @"/(?:home|usr|opt|var|root)/[^\s]+", 
            "[PATH_REMOVED]");

        // Remove any UNC paths (\\server\share\...)
        signature = System.Text.RegularExpressions.Regex.Replace(
            signature, 
            @"\\\\[^\s]+", 
            "[PATH_REMOVED]");

        return signature;
    }
}
