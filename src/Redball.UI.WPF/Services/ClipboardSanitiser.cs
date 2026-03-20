using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Redball.UI.Services;

/// <summary>
/// Scans text for sensitive patterns (credit cards, SSNs, API keys, etc.)
/// and returns warnings before TypeThing types the content.
/// </summary>
public static class ClipboardSanitiser
{
    /// <summary>Maximum clipboard length before a size warning is raised.</summary>
    public const int DefaultMaxSafeLength = 5000;

    private static readonly (string Name, Regex Pattern)[] SensitivePatterns =
    {
        ("Credit card number",   new Regex(@"\b(?:\d[ -]*?){13,19}\b", RegexOptions.Compiled)),
        ("Social Security Number", new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)),
        ("API key / secret",     new Regex(@"(?i)\b(?:api[_-]?key|secret|token|password)\s*[:=]\s*\S{8,}", RegexOptions.Compiled)),
        ("Private key header",   new Regex(@"-----BEGIN\s+(?:RSA\s+)?PRIVATE\s+KEY-----", RegexOptions.Compiled)),
        ("Bearer token",         new Regex(@"(?i)bearer\s+[a-z0-9\-_\.]{20,}", RegexOptions.Compiled)),
        ("AWS access key",       new Regex(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled)),
        ("Connection string",    new Regex(@"(?i)(?:server|data\s+source|host)\s*=\s*[^;]{3,}", RegexOptions.Compiled)),
    };

    /// <summary>
    /// Analyse text and return a list of human-readable warnings.
    /// Returns an empty list if nothing suspicious is found.
    /// </summary>
    public static List<string> Analyse(string text, int maxSafeLength = DefaultMaxSafeLength)
    {
        var warnings = new List<string>();

        if (string.IsNullOrEmpty(text))
            return warnings;

        if (text.Length > maxSafeLength)
            warnings.Add($"Clipboard is very large ({text.Length:N0} characters). This may take a while to type.");

        foreach (var (name, pattern) in SensitivePatterns)
        {
            if (pattern.IsMatch(text))
                warnings.Add($"Possible {name} detected in clipboard content.");
        }

        return warnings;
    }

    /// <summary>
    /// Returns true if the text passes sanitisation (no warnings or user accepted warnings).
    /// Pure analysis — does not show UI.
    /// </summary>
    public static bool IsSafe(string text, int maxSafeLength = DefaultMaxSafeLength)
        => Analyse(text, maxSafeLength).Count == 0;
}
