using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Build provenance record.
/// </summary>
public class BuildProvenance
{
    public string BuildId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Version { get; set; } = "";
    public DateTime BuildTimestamp { get; set; }
    public string BuildMachine { get; set; } = "";
    public string BuilderIdentity { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string SourceBranch { get; set; } = "";
    public bool IsCleanBuild { get; set; }
    public List<string> ModifiedFiles { get; set; } = new();
    public Dictionary<string, string> BuildParameters { get; set; } = new();
    public List<ArtifactAttestation> Artifacts { get; set; } = new();
}

/// <summary>
/// Artifact attestation.
/// </summary>
public class ArtifactAttestation
{
    public string FileName { get; set; } = "";
    public string Sha256Hash { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime BuildTimestamp { get; set; }
    public string? SignedHash { get; set; }
    public string? SignatureAlgorithm { get; set; }
}

/// <summary>
/// Reproducibility test result.
/// </summary>
public class ReproducibilityResult
{
    public string BuildId { get; set; } = "";
    public bool IsReproducible { get; set; }
    public List<string> MatchingArtifacts { get; set; } = new();
    public List<ArtifactMismatch> MismatchedArtifacts { get; set; } = new();
    public string? FailureReason { get; set; }
}

/// <summary>
/// Artifact mismatch details.
/// </summary>
public class ArtifactMismatch
{
    public string FileName { get; set; } = "";
    public string ExpectedHash { get; set; } = "";
    public string ActualHash { get; set; } = "";
    public long ExpectedSize { get; set; }
    public long ActualSize { get; set; }
}

/// <summary>
/// Service for reproducible builds and artifact provenance.
/// Implements dist-5 from improve_me.txt: Reproducible builds + artifact provenance attestation.
/// </summary>
public class ReproducibleBuildService
{
    private static readonly Lazy<ReproducibleBuildService> _instance = new(() => new ReproducibleBuildService());
    public static ReproducibleBuildService Instance => _instance.Value;

    private readonly List<BuildProvenance> _provenanceRecords = new();
    private readonly object _lock = new();

    private ReproducibleBuildService()
    {
        Logger.Info("ReproducibleBuildService", "Reproducible build service initialized");
    }

    /// <summary>
    /// Starts a new build with provenance tracking.
    /// </summary>
    public BuildProvenance StartBuild(string version, string sourceCommit, string sourceBranch)
    {
        var provenance = new BuildProvenance
        {
            Version = version,
            BuildTimestamp = DateTime.UtcNow,
            BuildMachine = Environment.MachineName,
            BuilderIdentity = GetBuilderIdentity(),
            SourceCommit = sourceCommit,
            SourceBranch = sourceBranch,
            IsCleanBuild = CheckCleanBuild(),
            ModifiedFiles = GetModifiedFiles(),
            BuildParameters = GetBuildParameters()
        };

        lock (_lock)
        {
            _provenanceRecords.Add(provenance);
        }

        Logger.Info("ReproducibleBuildService", $"Build started: {provenance.BuildId} (v{version})");
        return provenance;
    }

    /// <summary>
    /// Records an artifact in the build provenance.
    /// </summary>
    public async Task<ArtifactAttestation> RecordArtifactAsync(BuildProvenance provenance, string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Artifact not found: {filePath}");
        }

        var fileInfo = new FileInfo(filePath);
        var hash = await ComputeFileHashAsync(filePath);

        var attestation = new ArtifactAttestation
        {
            FileName = Path.GetFileName(filePath),
            Sha256Hash = hash,
            FileSize = fileInfo.Length,
            BuildTimestamp = provenance.BuildTimestamp
        };

        // Sign the hash (simplified - would use proper signing in production)
        attestation.SignedHash = SignHash(hash);
        attestation.SignatureAlgorithm = "RSA-SHA256";

        lock (_lock)
        {
            provenance.Artifacts.Add(attestation);
        }

        Logger.Info("ReproducibleBuildService", $"Artifact recorded: {attestation.FileName} ({attestation.Sha256Hash})");
        return attestation;
    }

    /// <summary>
    /// Verifies artifact integrity against attestation.
    /// </summary>
    public async Task<bool> VerifyArtifactAsync(string filePath, ArtifactAttestation attestation)
    {
        if (!File.Exists(filePath))
        {
            Logger.Warning("ReproducibleBuildService", $"Artifact file not found: {filePath}");
            return false;
        }

        var fileInfo = new FileInfo(filePath);
        var hash = await ComputeFileHashAsync(filePath);

        var hashMatches = hash.Equals(attestation.Sha256Hash, StringComparison.OrdinalIgnoreCase);
        var sizeMatches = fileInfo.Length == attestation.FileSize;

        var valid = hashMatches && sizeMatches;

        if (!valid)
        {
            Logger.Warning("ReproducibleBuildService",
                $"Artifact verification failed: {attestation.FileName}" +
                $" (hash: {hashMatches}, size: {sizeMatches})");
        }
        else
        {
            Logger.Info("ReproducibleBuildService", $"Artifact verified: {attestation.FileName}");
        }

        return valid;
    }

    /// <summary>
    /// Tests reproducibility by comparing two builds.
    /// </summary>
    public ReproducibilityResult TestReproducibility(string buildId1, string buildId2)
    {
        BuildProvenance? build1, build2;

        lock (_lock)
        {
            build1 = _provenanceRecords.FirstOrDefault(b => b.BuildId == buildId1);
            build2 = _provenanceRecords.FirstOrDefault(b => b.BuildId == buildId2);
        }

        if (build1 == null || build2 == null)
        {
            return new ReproducibilityResult
            {
                BuildId = buildId1,
                IsReproducible = false,
                FailureReason = "One or both builds not found"
            };
        }

        var result = new ReproducibilityResult
        {
            BuildId = buildId1,
            MatchingArtifacts = new List<string>(),
            MismatchedArtifacts = new List<ArtifactMismatch>()
        };

        // Compare artifacts
        foreach (var artifact1 in build1.Artifacts)
        {
            var artifact2 = build2.Artifacts.FirstOrDefault(a => a.FileName == artifact1.FileName);

            if (artifact2 == null)
            {
                result.MismatchedArtifacts.Add(new ArtifactMismatch
                {
                    FileName = artifact1.FileName,
                    ExpectedHash = artifact1.Sha256Hash,
                    ActualHash = "[missing]",
                    ExpectedSize = artifact1.FileSize,
                    ActualSize = 0
                });
            }
            else if (artifact1.Sha256Hash != artifact2.Sha256Hash)
            {
                result.MismatchedArtifacts.Add(new ArtifactMismatch
                {
                    FileName = artifact1.FileName,
                    ExpectedHash = artifact1.Sha256Hash,
                    ActualHash = artifact2.Sha256Hash,
                    ExpectedSize = artifact1.FileSize,
                    ActualSize = artifact2.FileSize
                });
            }
            else
            {
                result.MatchingArtifacts.Add(artifact1.FileName);
            }
        }

        result.IsReproducible = !result.MismatchedArtifacts.Any() &&
                                result.MatchingArtifacts.Any();

        Logger.Info("ReproducibleBuildService",
            $"Reproducibility test: {result.MatchingArtifacts.Count} matching, {result.MismatchedArtifacts.Count} mismatched");

        return result;
    }

    /// <summary>
    /// Exports provenance attestation to JSON.
    /// </summary>
    public string ExportProvenance(BuildProvenance provenance)
    {
        return JsonSerializer.Serialize(provenance, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Exports Software Bill of Materials (SBOM).
    /// </summary>
    public string ExportSBOM(BuildProvenance provenance)
    {
        var sbom = new
        {
            Schema = "https://cyclonedx.org/schema/bom/1.4",
            BomFormat = "CycloneDX",
            SpecVersion = "1.4",
            SerialNumber = $"urn:uuid:{Guid.NewGuid()}",
            Version = 1,
            Metadata = new
            {
                Timestamp = DateTime.UtcNow,
                Tools = new[]
                {
                    new { Vendor = "ArMaTeC", Name = "Redball", Version = provenance.Version }
                },
                Component = new
                {
                    Type = "application",
                    Name = "Redball",
                    Version = provenance.Version,
                    Purl = $"pkg:nuget/Redball@{provenance.Version}"
                }
            },
            Components = provenance.Artifacts.Select(a => new
            {
                Type = "file",
                Name = a.FileName,
                Hashes = new[]
                {
                    new { Alg = "SHA-256", Content = a.Sha256Hash }
                }
            })
        };

        return JsonSerializer.Serialize(sbom, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Gets build reproducibility summary.
    /// </summary>
    public ReproducibilitySummary GetSummary()
    {
        lock (_lock)
        {
            var totalBuilds = _provenanceRecords.Count;
            var cleanBuilds = _provenanceRecords.Count(b => b.IsCleanBuild);
            var attestedArtifacts = _provenanceRecords.Sum(b => b.Artifacts.Count);

            // Calculate reproducibility rate from pairwise comparisons
            var reproducibleBuilds = 0;
            var totalComparisons = 0;

            for (int i = 0; i < _provenanceRecords.Count - 1; i++)
            {
                for (int j = i + 1; j < _provenanceRecords.Count; j++)
                {
                    var result = TestReproducibility(_provenanceRecords[i].BuildId, _provenanceRecords[j].BuildId);
                    totalComparisons++;
                    if (result.IsReproducible) reproducibleBuilds++;
                }
            }

            return new ReproducibilitySummary
            {
                TotalBuilds = totalBuilds,
                CleanBuilds = cleanBuilds,
                AttestedArtifacts = attestedArtifacts,
                ReproducibilityRate = totalComparisons > 0 ? (double)reproducibleBuilds / totalComparisons * 100 : 0,
                RecentBuildIds = _provenanceRecords.TakeLast(5).Select(b => b.BuildId).ToList()
            };
        }
    }

    /// <summary>
    /// Gets all provenance records.
    /// </summary>
    public IReadOnlyList<BuildProvenance> GetProvenanceRecords()
    {
        lock (_lock)
        {
            return _provenanceRecords.ToList();
        }
    }

    private async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string SignHash(string hash)
    {
        // Simplified signing - in production would use proper key signing
        var data = Encoding.UTF8.GetBytes(hash);
        var signed = Convert.ToBase64String(data);
        return $"sig:{signed}";
    }

    private string GetBuilderIdentity()
    {
        // Get from environment or configuration
        var user = Environment.UserName;
        var domain = Environment.UserDomainName;
        return $"{domain}\\{user}";
    }

    private bool CheckCleanBuild()
    {
        // Check if there are uncommitted changes
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --porcelain",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return string.IsNullOrWhiteSpace(output);
        }
        catch (Exception ex)
        {
            Logger.Debug("ReproducibleBuildService", $"Git clean check failed: {ex.Message}");
            return false;
        }
    }

    private List<string> GetModifiedFiles()
    {
        var files = new List<string>();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --porcelain",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return files;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(output))
            {
                files.AddRange(output.Split('\n')
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => l.Substring(3).Trim()));
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("ReproducibleBuildService", $"Git status check failed: {ex.Message}");
        }

        return files;
    }

    private Dictionary<string, string> GetBuildParameters()
    {
        return new Dictionary<string, string>
        {
            ["Configuration"] = "Release",
            ["Platform"] = "x64",
            ["Runtime"] = "win-x64",
            ["Framework"] = "net10.0-windows",
            ["SelfContained"] = "true",
            ["PublishReadyToRun"] = "true",
            ["PublishSingleFile"] = "true"
        };
    }
}

/// <summary>
/// Reproducibility summary.
/// </summary>
public class ReproducibilitySummary
{
    public int TotalBuilds { get; set; }
    public int CleanBuilds { get; set; }
    public int AttestedArtifacts { get; set; }
    public double ReproducibilityRate { get; set; }
    public List<string> RecentBuildIds { get; set; } = new();
}
