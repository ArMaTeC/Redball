using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Result of a CI security gate check.
/// </summary>
public class SecurityGateResult
{
    public string GateName { get; set; } = "";
    public bool Passed { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public TimeSpan Duration { get; set; }
    public string? Details { get; set; }

    public void AddError(string message) => Errors.Add(message);
    public void AddWarning(string message) => Warnings.Add(message);
}

/// <summary>
/// Types of security CI gates.
/// </summary>
public enum SecurityGateType
{
    DependencyAudit,
    SecretScanning,
    SigningVerification,
    SBOMGeneration,
    ThreatModelValidation,
    TamperPolicyCheck,
    ConfigurationValidation,
    All
}

/// <summary>
/// Service for implementing security CI/CD gates.
/// Implements sec-6 from improve_me.txt: Security CI gates (dependency audit, secret scanning, signing verification, SBOM).
/// </summary>
public class SecurityCIGatesService
{
    private static readonly Lazy<SecurityCIGatesService> _instance = new(() => new SecurityCIGatesService());
    public static SecurityCIGatesService Instance => _instance.Value;

    private readonly List<string> _blockedSecretsPatterns = new()
    {
        // API keys and tokens
        @"api[_-]?key\s*[=:]\s*[a-zA-Z0-9]{20,}",
        @"api[_-]?secret\s*[=:]\s*[a-zA-Z0-9]{20,}",
        @"auth[_-]?token\s*[=:]\s*[a-zA-Z0-9]{20,}",
        @"bearer\s+[a-zA-Z0-9_\-]{20,}",
        @"password\s*[=:]\s*\S{8,}",
        @"connection[_-]?string\s*[=:]\s*.*password",
        
        // Private keys
        @"-----BEGIN (RSA |DSA |EC |OPENSSH )?PRIVATE KEY-----",
        @"private[_-]?key\s*[=:]\s*[a-zA-Z0-9]{20,}",
        
        // GitHub/GitLab tokens
        @"gh[pousr]_[A-Za-z0-9_]{36}",
        @"glpat-[A-Za-z0-9_\-]{20}",
        
        // AWS/Azure/GCP keys
        @"AKIA[0-9A-Z]{16}",
        @"azure[_-]?key\s*[=:]\s*[a-zA-Z0-9]{20,}",
        @"gcp[_-]?key\s*[=:]\s*[a-zA-Z0-9]{20,}",
        
        // Generic high-entropy strings that look like secrets
        @"[a-zA-Z0-9]{32,}[_-]?secret",
        @"secret[_-]?[a-zA-Z0-9]{32,}"
    };

    private readonly List<string> _dependencyVulnerabilities = new();

    private SecurityCIGatesService()
    {
        Logger.Info("SecurityCIGatesService", "Security CI gates service initialized");
    }

    /// <summary>
    /// Runs all configured security gates and returns aggregated results.
    /// </summary>
    public async Task<List<SecurityGateResult>> RunAllGatesAsync(string sourceDirectory, string? outputPath = null)
    {
        var results = new List<SecurityGateResult>();
        var stopwatch = Stopwatch.StartNew();

        Logger.Info("SecurityCIGatesService", "Starting security CI gates validation...");

        // Run each gate
        results.Add(await RunDependencyAuditAsync(sourceDirectory));
        results.Add(await RunSecretScanningAsync(sourceDirectory));
        results.Add(await RunSigningVerificationAsync(sourceDirectory));
        results.Add(await RunSBOMGenerationAsync(sourceDirectory, outputPath));
        results.Add(RunThreatModelValidation());
        results.Add(RunConfigurationValidation());

        stopwatch.Stop();

        // Generate report
        var summary = GenerateReport(results, stopwatch.Elapsed);
        Logger.Info("SecurityCIGatesService", summary);

        return results;
    }

    /// <summary>
    /// Runs dependency audit gate - checks for known vulnerabilities in dependencies.
    /// </summary>
    public async Task<SecurityGateResult> RunDependencyAuditAsync(string sourceDirectory)
    {
        var result = new SecurityGateResult { GateName = "Dependency Audit" };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.Info("SecurityCIGatesService", "Running dependency audit...");

            // Check for NuGet packages with known vulnerabilities
            var csprojFiles = Directory.GetFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories);
            var checkedPackages = 0;
            var vulnerablePackages = 0;

            foreach (var csproj in csprojFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(csproj);
                    
                    // Parse package references (simplified)
                    var packageMatches = Regex.Matches(content, 
                        @"<PackageReference\s+Include=""([^""]+)""\s+Version=""([^""]+)""");
                    
                    foreach (Match match in packageMatches)
                    {
                        checkedPackages++;
                        var packageName = match.Groups[1].Value;
                        var version = match.Groups[2].Value;

                        // Check against known vulnerable versions
                        if (IsKnownVulnerablePackage(packageName, version))
                        {
                            vulnerablePackages++;
                            result.AddError($"Vulnerable package: {packageName}@{version} in {Path.GetFileName(csproj)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.AddWarning($"Could not analyze {csproj}: {ex.Message}");
                }
            }

            // Check for dotnet-outdated-tool or similar
            result.Details = $"Checked {checkedPackages} packages, found {vulnerablePackages} potential vulnerabilities";
            
            if (vulnerablePackages == 0)
            {
                result.Passed = true;
                Logger.Info("SecurityCIGatesService", "Dependency audit passed - no known vulnerabilities detected");
            }
            else
            {
                result.Passed = false;
                Logger.Warning("SecurityCIGatesService", $"Dependency audit failed - {vulnerablePackages} vulnerable packages found");
            }
        }
        catch (Exception ex)
        {
            result.AddError($"Dependency audit failed: {ex.Message}");
            result.Passed = false;
            Logger.Error("SecurityCIGatesService", "Dependency audit error", ex);
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;
        return result;
    }

    /// <summary>
    /// Runs secret scanning gate - checks for hardcoded secrets in source code.
    /// </summary>
    public async Task<SecurityGateResult> RunSecretScanningAsync(string sourceDirectory)
    {
        var result = new SecurityGateResult { GateName = "Secret Scanning" };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.Info("SecurityCIGatesService", "Running secret scanning...");

            var filesScanned = 0;
            var secretsFound = 0;
            var extensions = new[] { ".cs", ".json", ".xml", ".config", ".yml", ".yaml", ".ps1", ".bat", ".cmd" };
            
            var files = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\") && !f.Contains("\\.git\\"));

            foreach (var file in files)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    filesScanned++;

                    foreach (var pattern in _blockedSecretsPatterns)
                    {
                        var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
                        foreach (Match match in matches)
                        {
                            // Check if it's a false positive (variable name only, not actual secret)
                            var line = GetLineFromPosition(content, match.Index);
                            if (!IsLikelyFalsePositive(line, match.Value))
                            {
                                secretsFound++;
                                var relativePath = file.Replace(sourceDirectory, "").TrimStart('\\', '/');
                                result.AddError($"Potential secret in {relativePath}: {match.Value[..Math.Min(20, match.Value.Length)]}...");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.AddWarning($"Could not scan {file}: {ex.Message}");
                }
            }

            result.Details = $"Scanned {filesScanned} files, found {secretsFound} potential secrets";
            
            // Secret scanning is a hard gate - any secrets found = fail
            result.Passed = secretsFound == 0;
            
            if (result.Passed)
            {
                Logger.Info("SecurityCIGatesService", "Secret scanning passed - no hardcoded secrets detected");
            }
            else
            {
                Logger.Error("SecurityCIGatesService", $"Secret scanning failed - {secretsFound} potential secrets found");
            }
        }
        catch (Exception ex)
        {
            result.AddError($"Secret scanning failed: {ex.Message}");
            result.Passed = false;
            Logger.Error("SecurityCIGatesService", "Secret scanning error", ex);
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;
        return result;
    }

    /// <summary>
    /// Runs signing verification gate - verifies all binaries are properly signed.
    /// </summary>
    public async Task<SecurityGateResult> RunSigningVerificationAsync(string sourceDirectory)
    {
        var result = new SecurityGateResult { GateName = "Signing Verification" };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.Info("SecurityCIGatesService", "Running signing verification...");

            // Look for output binaries
            var distDir = Path.Combine(sourceDirectory, "dist");
            if (!Directory.Exists(distDir))
            {
                distDir = Path.Combine(sourceDirectory, "src", "Redball.UI.WPF", "bin", "Release");
            }

            if (!Directory.Exists(distDir))
            {
                result.AddWarning("Distribution directory not found - skipping signing verification");
                result.Passed = true; // Soft gate - skip if no binaries
                result.Details = "No binaries to verify";
                return result;
            }

            var binaries = Directory.GetFiles(distDir, "*.exe", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(distDir, "*.dll", SearchOption.TopDirectoryOnly))
                .Where(f => Path.GetFileName(f).StartsWith("Redball", StringComparison.OrdinalIgnoreCase));

            var checkedCount = 0;
            var signedCount = 0;

            foreach (var binary in binaries)
            {
                checkedCount++;
                
                // Use SecurityService for verification
                if (SecurityService.VerifyAuthenticodeSignature(binary))
                {
                    signedCount++;
                }
                else
                {
                    var relativePath = binary.Replace(sourceDirectory, "").TrimStart('\\', '/');
                    result.AddError($"Binary not signed: {relativePath}");
                }
            }

            result.Details = $"Checked {checkedCount} binaries, {signedCount} properly signed";
            result.Passed = checkedCount == 0 || signedCount == checkedCount;

            if (result.Passed)
            {
                Logger.Info("SecurityCIGatesService", "Signing verification passed");
            }
            else
            {
                Logger.Warning("SecurityCIGatesService", $"Signing verification failed - {checkedCount - signedCount} unsigned binaries");
            }
        }
        catch (Exception ex)
        {
            result.AddError($"Signing verification failed: {ex.Message}");
            result.Passed = false;
            Logger.Error("SecurityCIGatesService", "Signing verification error", ex);
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;
        return result;
    }

    /// <summary>
    /// Runs SBOM generation gate - generates and validates Software Bill of Materials.
    /// </summary>
    public async Task<SecurityGateResult> RunSBOMGenerationAsync(string sourceDirectory, string? outputPath = null)
    {
        var result = new SecurityGateResult { GateName = "SBOM Generation" };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.Info("SecurityCIGatesService", "Running SBOM generation...");

            // Generate SBOM using SecurityService
            var sbom = SecurityService.GenerateSBOM();
            
            if (sbom == "{}")
            {
                result.AddError("SBOM generation failed - empty output");
                result.Passed = false;
            }
            else
            {
                // Validate SBOM structure
                try
                {
                    var doc = JsonDocument.Parse(sbom);
                    var hasPackages = doc.RootElement.TryGetProperty("packages", out _);
                    var hasRelationships = doc.RootElement.TryGetProperty("relationships", out _);
                    
                    if (!hasPackages || !hasRelationships)
                    {
                        result.AddWarning("SBOM missing required fields (packages or relationships)");
                    }

                    // Save to file if output path provided
                    if (!string.IsNullOrEmpty(outputPath))
                    {
                        var sbomPath = Path.Combine(outputPath, $"sbom-{DateTime.Now:yyyyMMdd}.json");
                        await File.WriteAllTextAsync(sbomPath, sbom);
                        result.Details = $"SBOM generated and saved to {sbomPath}";
                        Logger.Info("SecurityCIGatesService", $"SBOM saved to {sbomPath}");
                    }
                    else
                    {
                        result.Details = "SBOM generated successfully";
                    }

                    result.Passed = true;
                }
                catch (JsonException)
                {
                    result.AddError("Generated SBOM is not valid JSON");
                    result.Passed = false;
                }
            }
        }
        catch (Exception ex)
        {
            result.AddError($"SBOM generation failed: {ex.Message}");
            result.Passed = false;
            Logger.Error("SecurityCIGatesService", "SBOM generation error", ex);
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;
        return result;
    }

    /// <summary>
    /// Runs threat model validation gate.
    /// </summary>
    public SecurityGateResult RunThreatModelValidation()
    {
        var result = new SecurityGateResult { GateName = "Threat Model Validation" };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.Info("SecurityCIGatesService", "Running threat model validation...");

            var summary = ThreatModelService.Instance.GetSummary();
            
            // Critical: All critical-risk threats must be mitigated
            if (summary.CriticalRiskCount > 0)
            {
                result.AddError($"{summary.CriticalRiskCount} critical-risk threats are unmitigated");
            }

            // Warning: High-risk threats should be mitigated
            if (summary.HighRiskCount > 0)
            {
                result.AddWarning($"{summary.HighRiskCount} high-risk threats are unmitigated");
            }

            result.Details = $"{summary.TotalThreats} threats, {summary.MitigatedCount} mitigated, {summary.UnmitigatedCount} pending";
            
            // Gate passes if no critical unmitigated threats
            result.Passed = summary.CriticalRiskCount == 0;

            if (result.Passed)
            {
                Logger.Info("SecurityCIGatesService", "Threat model validation passed");
            }
            else
            {
                Logger.Warning("SecurityCIGatesService", "Threat model validation failed - critical threats unmitigated");
            }
        }
        catch (Exception ex)
        {
            result.AddError($"Threat model validation failed: {ex.Message}");
            result.Passed = false;
            Logger.Error("SecurityCIGatesService", "Threat model validation error", ex);
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;
        return result;
    }

    /// <summary>
    /// Runs configuration validation gate.
    /// </summary>
    public SecurityGateResult RunConfigurationValidation()
    {
        var result = new SecurityGateResult { GateName = "Configuration Validation" };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.Info("SecurityCIGatesService", "Running configuration validation...");

            var config = ConfigService.Instance.Config;
            
            // Verify security settings are enabled
            if (!config.EncryptConfig)
            {
                result.AddWarning("Config encryption is disabled");
            }

            if (!config.VerifyUpdateSignature)
            {
                result.AddWarning("Update signature verification is disabled");
            }

            // Check for default/weak settings
            if (config.DefaultDuration < 1 || config.DefaultDuration > 720)
            {
                result.AddWarning("Default duration outside recommended range");
            }

            result.Details = "Configuration security settings validated";
            result.Passed = true; // Warnings don't fail the gate

            Logger.Info("SecurityCIGatesService", "Configuration validation completed");
        }
        catch (Exception ex)
        {
            result.AddError($"Configuration validation failed: {ex.Message}");
            result.Passed = false;
            Logger.Error("SecurityCIGatesService", "Configuration validation error", ex);
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;
        return result;
    }

    /// <summary>
    /// Generates a comprehensive report from all gate results.
    /// </summary>
    private string GenerateReport(List<SecurityGateResult> results, TimeSpan totalDuration)
    {
        var passed = results.Count(r => r.Passed);
        var failed = results.Count(r => !r.Passed);
        var totalErrors = results.Sum(r => r.Errors.Count);
        var totalWarnings = results.Sum(r => r.Warnings.Count);

        var sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("SECURITY CI GATES REPORT");
        sb.AppendLine("========================================");
        sb.AppendLine($"Total Duration: {totalDuration.TotalSeconds:F2}s");
        sb.AppendLine($"Gates Passed: {passed}/{results.Count}");
        sb.AppendLine($"Gates Failed: {failed}/{results.Count}");
        sb.AppendLine($"Total Errors: {totalErrors}");
        sb.AppendLine($"Total Warnings: {totalWarnings}");
        sb.AppendLine();

        foreach (var result in results)
        {
            var status = result.Passed ? "PASS" : "FAIL";
            sb.AppendLine($"[{status}] {result.GateName} ({result.Duration.TotalMilliseconds:F0}ms)");
            
            if (!string.IsNullOrEmpty(result.Details))
            {
                sb.AppendLine($"       Details: {result.Details}");
            }

            foreach (var error in result.Errors)
            {
                sb.AppendLine($"       ERROR: {error}");
            }

            foreach (var warning in result.Warnings)
            {
                sb.AppendLine($"       WARNING: {warning}");
            }
        }

        sb.AppendLine("========================================");
        sb.AppendLine(failed == 0 ? "ALL GATES PASSED" : "SOME GATES FAILED - REVIEW REQUIRED");
        sb.AppendLine("========================================");

        return sb.ToString();
    }

    /// <summary>
    /// Checks if a package/version combination has known vulnerabilities.
    /// </summary>
    private bool IsKnownVulnerablePackage(string packageName, string version)
    {
        // This is a simplified check - in production, this would query a vulnerability database
        // like GitHub Advisory Database or NuGet's vulnerability feed
        var vulnerablePackages = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Newtonsoft.Json", new[] { "12.0.1", "12.0.2", "12.0.3" } }, // Example
            { "System.Text.Json", new[] { "6.0.0", "7.0.0" } }, // Example
        };

        if (vulnerablePackages.TryGetValue(packageName, out var vulnerableVersions))
        {
            return vulnerableVersions.Contains(version);
        }

        return false;
    }

    /// <summary>
    /// Gets the line of text containing a specific position.
    /// </summary>
    private string GetLineFromPosition(string text, int position)
    {
        var start = text.LastIndexOf('\n', position) + 1;
        var end = text.IndexOf('\n', position);
        if (end == -1) end = text.Length;
        return text[start..end].Trim();
    }

    /// <summary>
    /// Determines if a match is likely a false positive.
    /// </summary>
    private bool IsLikelyFalsePositive(string line, string match)
    {
        // Check for common false positive patterns
        var falsePositivePatterns = new[]
        {
            "placeholder",
            "example",
            "sample",
            "test",
            "demo",
            "mock",
            "fake",
            "dummy",
            "TODO",
            "FIXME",
            "config.GetValue",
            "Environment.GetEnvironmentVariable",
            "_secretProvider"
        };

        var lowerLine = line.ToLowerInvariant();
        return falsePositivePatterns.Any(fp => lowerLine.Contains(fp.ToLowerInvariant()));
    }

    /// <summary>
    /// Exports gate results to a JSON file.
    /// </summary>
    public bool ExportResultsToJson(List<SecurityGateResult> results, string filePath)
    {
        try
        {
            var export = new
            {
                GeneratedAt = DateTime.UtcNow,
                Results = results.Select(r => new
                {
                    r.GateName,
                    r.Passed,
                    r.Errors,
                    r.Warnings,
                    DurationMs = r.Duration.TotalMilliseconds,
                    r.Details
                })
            };

            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            File.WriteAllText(filePath, json);
            Logger.Info("SecurityCIGatesService", $"Results exported to: {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("SecurityCIGatesService", "Failed to export results", ex);
            return false;
        }
    }
}
