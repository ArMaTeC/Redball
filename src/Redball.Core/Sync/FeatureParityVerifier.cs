using System;
using System.Collections.Generic;
using System.Linq;

namespace Redball.Core.Sync;

/// <summary>
/// Feature parity verification service that tracks which features are
/// available and working across Windows, macOS, and Linux platforms.
/// </summary>
public class FeatureParityVerifier
{
    private static readonly Lazy<FeatureParityVerifier> _instance = new(() => new FeatureParityVerifier());
    public static FeatureParityVerifier Instance => _instance.Value;

    private readonly List<FeatureDefinition> _featureDefinitions;

    private FeatureParityVerifier()
    {
        _featureDefinitions = InitializeFeatureDefinitions();
        Logger.Debug("FeatureParityVerifier", "Initialized");
    }

    /// <summary>
    /// Gets the complete feature parity matrix for all platforms.
    /// </summary>
    public FeatureParityMatrix GetParityMatrix()
    {
        var matrix = new FeatureParityMatrix
        {
            GeneratedAt = DateTime.UtcNow,
            Platforms = new[] { "Windows", "macOS", "Linux" },
            Features = new List<FeatureParityEntry>()
        };

        foreach (var feature in _featureDefinitions)
        {
            var entry = new FeatureParityEntry
            {
                FeatureName = feature.Name,
                Category = feature.Category,
                Description = feature.Description,
                PlatformStatus = new Dictionary<string, FeatureStatus>()
            };

            // Check each platform
            entry.PlatformStatus["Windows"] = GetFeatureStatus(feature, "windows");
            entry.PlatformStatus["macOS"] = GetFeatureStatus(feature, "macos");
            entry.PlatformStatus["Linux"] = GetFeatureStatus(feature, "linux");

            // Calculate parity score
            entry.ParityScore = CalculateParityScore(entry.PlatformStatus);
            entry.IsFullySupported = entry.PlatformStatus.Values.All(s => s == FeatureStatus.Available);
            entry.HasPartialSupport = entry.PlatformStatus.Values.Any(s => s == FeatureStatus.Available) &&
                                       !entry.IsFullySupported;

            matrix.Features.Add(entry);
        }

        // Calculate overall statistics
        matrix.TotalFeatures = matrix.Features.Count;
        matrix.FullySupportedFeatures = matrix.Features.Count(f => f.IsFullySupported);
        matrix.PartiallySupportedFeatures = matrix.Features.Count(f => f.HasPartialSupport);
        matrix.OverallParityPercentage = (double)matrix.FullySupportedFeatures / matrix.TotalFeatures * 100;

        return matrix;
    }

    /// <summary>
    /// Verifies a specific feature is available on the current platform.
    /// </summary>
    public bool IsFeatureAvailable(string featureName)
    {
        var currentPlatform = GetCurrentPlatform();
        var feature = _featureDefinitions.FirstOrDefault(f => f.Name == featureName);
        
        if (feature == null)
            return false;

        var status = GetFeatureStatus(feature, currentPlatform);
        return status == FeatureStatus.Available;
    }

    /// <summary>
    /// Gets features missing from the current platform.
    /// </summary>
    public List<string> GetMissingFeatures()
    {
        var currentPlatform = GetCurrentPlatform();
        
        return _featureDefinitions
            .Where(f => GetFeatureStatus(f, currentPlatform) != FeatureStatus.Available)
            .Select(f => f.Name)
            .ToList();
    }

    /// <summary>
    /// Generates a report of platform-specific gaps.
    /// </summary>
    public ParityGapReport GenerateGapReport()
    {
        var matrix = GetParityMatrix();
        var report = new ParityGapReport
        {
            GeneratedAt = DateTime.UtcNow,
            Gaps = new List<PlatformGap>()
        };

        // Windows gaps
        var windowsMissing = matrix.Features
            .Where(f => f.PlatformStatus["Windows"] != FeatureStatus.Available)
            .Select(f => f.FeatureName)
            .ToList();
        if (windowsMissing.Any())
        {
            report.Gaps.Add(new PlatformGap
            {
                Platform = "Windows",
                MissingFeatures = windowsMissing,
                Priority = GapPriority.Low // Windows is reference implementation
            });
        }

        // macOS gaps
        var macosMissing = matrix.Features
            .Where(f => f.PlatformStatus["macOS"] != FeatureStatus.Available)
            .Select(f => f.FeatureName)
            .ToList();
        if (macosMissing.Any())
        {
            report.Gaps.Add(new PlatformGap
            {
                Platform = "macOS",
                MissingFeatures = macosMissing,
                Priority = macosMissing.Count > 5 ? GapPriority.High : GapPriority.Medium
            });
        }

        // Linux gaps
        var linuxMissing = matrix.Features
            .Where(f => f.PlatformStatus["Linux"] != FeatureStatus.Available)
            .Select(f => f.FeatureName)
            .ToList();
        if (linuxMissing.Any())
        {
            report.Gaps.Add(new PlatformGap
            {
                Platform = "Linux",
                MissingFeatures = linuxMissing,
                Priority = linuxMissing.Count > 5 ? GapPriority.High : GapPriority.Medium
            });
        }

        return report;
    }

    /// <summary>
    /// Exports parity matrix as markdown for documentation.
    /// </summary>
    public string ExportAsMarkdown()
    {
        var matrix = GetParityMatrix();
        var markdown = new System.Text.StringBuilder();

        markdown.AppendLine("# Redball Feature Parity Matrix");
        markdown.AppendLine();
        markdown.AppendLine($"*Generated: {matrix.GeneratedAt:yyyy-MM-dd HH:mm:ss UTC}*");
        markdown.AppendLine();
        
        // Summary
        markdown.AppendLine("## Summary");
        markdown.AppendLine();
        markdown.AppendLine($"- **Overall Parity**: {matrix.OverallParityPercentage:F1}%");
        markdown.AppendLine($"- **Total Features**: {matrix.TotalFeatures}");
        markdown.AppendLine($"- **Fully Supported**: {matrix.FullySupportedFeatures}");
        markdown.AppendLine($"- **Partially Supported**: {matrix.PartiallySupportedFeatures}");
        markdown.AppendLine();

        // Legend
        markdown.AppendLine("## Legend");
        markdown.AppendLine();
        markdown.AppendLine("- ✅ Available - Feature fully implemented");
        markdown.AppendLine("- ⚠️ Partial - Feature partially implemented or has limitations");
        markdown.AppendLine("- ❌ Missing - Feature not yet implemented");
        markdown.AppendLine("- 🚫 N/A - Feature not applicable to this platform");
        markdown.AppendLine();

        // Matrix by category
        var categories = matrix.Features.GroupBy(f => f.Category);
        foreach (var category in categories)
        {
            markdown.AppendLine($"## {category.Key}");
            markdown.AppendLine();
            markdown.AppendLine("| Feature | Windows | macOS | Linux | Notes |");
            markdown.AppendLine("|---------|---------|-------|-------|-------|");

            foreach (var feature in category.OrderBy(f => f.FeatureName))
            {
                var windows = GetStatusEmoji(feature.PlatformStatus["Windows"]);
                var macos = GetStatusEmoji(feature.PlatformStatus["macOS"]);
                var linux = GetStatusEmoji(feature.PlatformStatus["Linux"]);
                var notes = GetFeatureNotes(feature);

                markdown.AppendLine($"| {feature.FeatureName} | {windows} | {macos} | {linux} | {notes} |");
            }

            markdown.AppendLine();
        }

        return markdown.ToString();
    }

    private List<FeatureDefinition> InitializeFeatureDefinitions()
    {
        return new List<FeatureDefinition>
        {
            // Core Keep-Awake
            new FeatureDefinition
            {
                Name = "Keep-Awake Engine",
                Category = "Core",
                Description = "Prevent system sleep",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Available,
                LinuxStatus = FeatureStatus.Available
            },
            new FeatureDefinition
            {
                Name = "Timed Sessions",
                Category = "Core",
                Description = "Session duration limits",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Available,
                LinuxStatus = FeatureStatus.Available
            },
            new FeatureDefinition
            {
                Name = "Heartbeat Input",
                Category = "Core",
                Description = "Simulate input to prevent sleep",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Available,
                LinuxStatus = FeatureStatus.Available
            },

            // Awareness Features
            new FeatureDefinition
            {
                Name = "Battery-Aware Mode",
                Category = "Awareness",
                Description = "Auto-pause on low battery",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Available,
                LinuxStatus = FeatureStatus.Available
            },
            new FeatureDefinition
            {
                Name = "Network-Aware Mode",
                Category = "Awareness",
                Description = "Pause when network unavailable",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Available,
                LinuxStatus = FeatureStatus.Available
            },
            new FeatureDefinition
            {
                Name = "Idle Detection",
                Category = "Awareness",
                Description = "Pause when user is idle",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Available,
                LinuxStatus = FeatureStatus.Available
            },
            new FeatureDefinition
            {
                Name = "Meeting Detection (Teams)",
                Category = "Awareness",
                Description = "Detect Teams meetings",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.NotApplicable,
                LinuxStatus = FeatureStatus.NotApplicable,
                Notes = "Windows-specific integration"
            },
            new FeatureDefinition
            {
                Name = "Meeting Detection (Slack)",
                Category = "Awareness",
                Description = "Detect Slack huddles",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.NotApplicable,
                LinuxStatus = FeatureStatus.NotApplicable,
                Notes = "Windows-specific integration"
            },
            new FeatureDefinition
            {
                Name = "Meeting Detection (Zoom)",
                Category = "Awareness",
                Description = "Detect Zoom meetings",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.NotApplicable,
                LinuxStatus = FeatureStatus.NotApplicable,
                Notes = "Windows-specific integration"
            },

            // UI Features
            new FeatureDefinition
            {
                Name = "System Tray / Menu Bar",
                Category = "UI",
                Description = "Tray/menubar integration",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Available,
                LinuxStatus = FeatureStatus.Available
            },
            new FeatureDefinition
            {
                Name = "Mini Widget",
                Category = "UI",
                Description = "Floating widget interface",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Planned,
                LinuxStatus = FeatureStatus.Planned
            },
            new FeatureDefinition
            {
                Name = "Command Palette",
                Category = "UI",
                Description = "Quick command interface",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Planned,
                LinuxStatus = FeatureStatus.Planned
            },
            new FeatureDefinition
            {
                Name = "HUD Overlay",
                Category = "UI",
                Description = "Head-up display",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Planned,
                LinuxStatus = FeatureStatus.Planned
            },

            // Advanced Features
            new FeatureDefinition
            {
                Name = "TypeThing (Text Input)",
                Category = "Advanced",
                Description = "Automated text typing",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Planned,
                LinuxStatus = FeatureStatus.Planned
            },
            new FeatureDefinition
            {
                Name = "Pomodoro Timer",
                Category = "Advanced",
                Description = "Focus time management",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Available,
                LinuxStatus = FeatureStatus.Available
            },
            new FeatureDefinition
            {
                Name = "Smart Schedule Learning",
                Category = "Advanced",
                Description = "AI-powered pattern detection",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Planned,
                LinuxStatus = FeatureStatus.Planned
            },
            new FeatureDefinition
            {
                Name = "Advanced Analytics",
                Category = "Advanced",
                Description = "Usage insights and predictions",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Planned,
                LinuxStatus = FeatureStatus.Planned
            },

            // Enterprise Features
            new FeatureDefinition
            {
                Name = "Team Settings Sync",
                Category = "Enterprise",
                Description = "Cloud-based team config",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Planned,
                LinuxStatus = FeatureStatus.Planned
            },
            new FeatureDefinition
            {
                Name = "Admin Dashboard",
                Category = "Enterprise",
                Description = "Usage reports for IT",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Planned,
                LinuxStatus = FeatureStatus.Planned
            },
            new FeatureDefinition
            {
                Name = "Audit Logging",
                Category = "Enterprise",
                Description = "Compliance logging",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Planned,
                LinuxStatus = FeatureStatus.Planned
            },

            // Cross-Platform
            new FeatureDefinition
            {
                Name = "Shared Configuration",
                Category = "Cross-Platform",
                Description = "Universal config format",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Available,
                LinuxStatus = FeatureStatus.Available
            },
            new FeatureDefinition
            {
                Name = "Cross-Platform Analytics",
                Category = "Cross-Platform",
                Description = "Unified analytics across platforms",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Available,
                LinuxStatus = FeatureStatus.Available
            },
            new FeatureDefinition
            {
                Name = "Browser Extension",
                Category = "Cross-Platform",
                Description = "Chrome/Edge/Firefox extension",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Available,
                LinuxStatus = FeatureStatus.Available
            },

            // Plugin System
            new FeatureDefinition
            {
                Name = "Plugin Architecture",
                Category = "Extensibility",
                Description = "Third-party plugin support",
                WindowsStatus = FeatureStatus.Available,
                MacOSStatus = FeatureStatus.Planned,
                LinuxStatus = FeatureStatus.Planned
            }
        };
    }

    private FeatureStatus GetFeatureStatus(FeatureDefinition feature, string platform)
    {
        return platform.ToLower() switch
        {
            "windows" => feature.WindowsStatus,
            "macos" => feature.MacOSStatus,
            "linux" => feature.LinuxStatus,
            _ => FeatureStatus.Missing
        };
    }

    private double CalculateParityScore(Dictionary<string, FeatureStatus> statuses)
    {
        var availableCount = statuses.Count(s => s.Value == FeatureStatus.Available);
        var naCount = statuses.Count(s => s.Value == FeatureStatus.NotApplicable);
        var relevantCount = statuses.Count - naCount;

        if (relevantCount == 0) return 1.0; // All N/A = perfect parity

        return (double)availableCount / relevantCount;
    }

    private string GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "macos";
        if (OperatingSystem.IsLinux()) return "linux";
        return "unknown";
    }

    private string GetStatusEmoji(FeatureStatus status)
    {
        return status switch
        {
            FeatureStatus.Available => "✅",
            FeatureStatus.Partial => "⚠️",
            FeatureStatus.Planned => "📋",
            FeatureStatus.Missing => "❌",
            FeatureStatus.NotApplicable => "🚫",
            _ => "❓"
        };
    }

    private string GetFeatureNotes(FeatureParityEntry feature)
    {
        var notes = new List<string>();

        var definition = _featureDefinitions.FirstOrDefault(f => f.Name == feature.FeatureName);
        if (definition?.Notes != null)
        {
            notes.Add(definition.Notes);
        }

        // Add platform-specific notes
        foreach (var status in feature.PlatformStatus)
        {
            if (status.Value == FeatureStatus.Partial)
            {
                notes.Add($"{status.Key}: partial support");
            }
        }

        return string.Join("; ", notes);
    }
}

/// <summary>
/// Feature definition with platform-specific status.
/// </summary>
public class FeatureDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Notes { get; set; }
    
    public FeatureStatus WindowsStatus { get; set; } = FeatureStatus.Missing;
    public FeatureStatus MacOSStatus { get; set; } = FeatureStatus.Missing;
    public FeatureStatus LinuxStatus { get; set; } = FeatureStatus.Missing;
}

public enum FeatureStatus
{
    Available,      // ✅ Fully implemented
    Partial,        // ⚠️ Partially implemented
    Planned,        // 📋 On roadmap
    Missing,        // ❌ Not implemented
    NotApplicable   // 🚫 Not applicable to platform
}

/// <summary>
/// Complete feature parity matrix.
/// </summary>
public class FeatureParityMatrix
{
    public DateTime GeneratedAt { get; set; }
    public string[] Platforms { get; set; } = Array.Empty<string>();
    public List<FeatureParityEntry> Features { get; set; } = new();
    
    public int TotalFeatures { get; set; }
    public int FullySupportedFeatures { get; set; }
    public int PartiallySupportedFeatures { get; set; }
    public double OverallParityPercentage { get; set; }
}

public class FeatureParityEntry
{
    public string FeatureName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, FeatureStatus> PlatformStatus { get; set; } = new();
    public double ParityScore { get; set; }
    public bool IsFullySupported { get; set; }
    public bool HasPartialSupport { get; set; }
}

/// <summary>
/// Platform gap report.
/// </summary>
public class ParityGapReport
{
    public DateTime GeneratedAt { get; set; }
    public List<PlatformGap> Gaps { get; set; } = new();
}

public class PlatformGap
{
    public string Platform { get; set; } = string.Empty;
    public List<string> MissingFeatures { get; set; } = new();
    public GapPriority Priority { get; set; }
}

public enum GapPriority
{
    Low,
    Medium,
    High,
    Critical
}
