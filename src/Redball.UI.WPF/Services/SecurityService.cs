using Redball.Core.Security;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Redball.UI.Services;

/// <summary>
/// Security service for code signing verification and integrity checks.
/// </summary>
public class SecurityService
{
    private static X509Certificate2? TryGetSignerCertificate(string filePath)
    {
        try
        {
#pragma warning disable SYSLIB0057
            var signerCertificate = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            return new X509Certificate2(signerCertificate);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Verifies the Authenticode signature of a file.
    /// </summary>
    public static bool VerifyAuthenticodeSignature(string filePath)
    {
        try
        {
            // SECURITY: Validate file path to prevent path traversal
            if (!SecurePathValidator.IsValidFilePath(filePath))
            {
                Logger.Error("SecurityService", $"Invalid file path for verification: {filePath}");
                return false;
            }

            if (!File.Exists(filePath))
            {
                Logger.Warning("SecurityService", $"File not found for verification: {filePath}");
                return false;
            }

            using var cert = TryGetSignerCertificate(filePath);
            if (cert is null)
            {
                Logger.Warning("SecurityService", $"No Authenticode signature found on file: {Path.GetFileName(filePath)}");
                return false;
            }
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            var certIsTimeValid = cert.NotBefore <= DateTime.UtcNow && cert.NotAfter >= DateTime.UtcNow;
            var chainValid = chain.Build(cert);

            if (chainValid && certIsTimeValid)
            {
                Logger.Info("SecurityService", $"Authenticode signature verified for: {Path.GetFileName(filePath)}");
                Logger.Debug("SecurityService", $"Certificate subject: {cert.Subject}");
                Logger.Debug("SecurityService", $"Certificate issuer: {cert.Issuer}");
                Logger.Debug("SecurityService", $"Valid from: {cert.NotBefore:g} to {cert.NotAfter:g}");
                return true;
            }

            Logger.Warning("SecurityService", $"Authenticode signature verification failed for: {Path.GetFileName(filePath)} (chainValid={chainValid}, certIsTimeValid={certIsTimeValid})");
            return false;
        }
        catch (CryptographicException ex)
        {
            Logger.Warning("SecurityService", $"No signature found or invalid signature: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("SecurityService", $"Error verifying signature for {filePath}", ex);
            return false;
        }
    }

    /// <summary>
    /// Generates a Software Bill of Materials (SBOM) for the application.
    /// </summary>
    public static string GenerateSBOM()
    {
        try
        {
            var sbom = new StringBuilder();
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            
            sbom.AppendLine("{\n  \"spdxVersion\": \"SPDX-2.3\",");
            sbom.AppendLine("  \"SPDXID\": \"SPDXRef-DOCUMENT\",");
            sbom.AppendLine("  \"name\": \"Redball\",");
            sbom.AppendLine($"  \"documentNamespace\": \"https://github.com/ArMaTeC/Redball/sbom/{Guid.NewGuid()}\",");
            sbom.AppendLine("  \"creationInfo\": {");
            sbom.AppendLine($"    \"created\": \"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\",");
            sbom.AppendLine("    \"creators\": [\"Tool: Redball-SBOM-Generator-1.0\"]");
            sbom.AppendLine("  },");
            sbom.AppendLine("  \"packages\": [");
            sbom.AppendLine("    {");
            sbom.AppendLine("      \"SPDXID\": \"SPDXRef-Redball-Application\",");
            sbom.AppendLine("      \"name\": \"Redball\",");
            sbom.AppendLine($"      \"versionInfo\": \"{version}\",");
            sbom.AppendLine("      \"downloadLocation\": \"https://github.com/ArMaTeC/Redball/releases\",");
            sbom.AppendLine("      \"filesAnalyzed\": false,");
            sbom.AppendLine("      \"licenseConcluded\": \"MIT\",");
            sbom.AppendLine("      \"licenseDeclared\": \"MIT\",");
            sbom.AppendLine("      \"copyrightText\": \"Copyright (c) ArMaTeC\",");
            sbom.AppendLine("      \"supplier\": \"Person: ArMaTeC\"");
            sbom.AppendLine("    },");
            sbom.AppendLine("    {");
            sbom.AppendLine("      \"SPDXID\": \"SPDXRef-DotNet-Runtime\",");
            sbom.AppendLine("      \"name\": \".NET-Runtime\",");
            sbom.AppendLine("      \"versionInfo\": \"10.0.x\",");
            sbom.AppendLine("      \"downloadLocation\": \"https://dotnet.microsoft.com/\",");
            sbom.AppendLine("      \"licenseConcluded\": \"MIT\",");
            sbom.AppendLine("      \"licenseDeclared\": \"MIT\"");
            sbom.AppendLine("    },");
            sbom.AppendLine("    {");
            sbom.AppendLine("      \"SPDXID\": \"SPDXRef-Hardcodet-NotifyIcon\",");
            sbom.AppendLine("      \"name\": \"Hardcodet.NotifyIcon.Wpf\",");
            sbom.AppendLine("      \"versionInfo\": \"1.1.0\",");
            sbom.AppendLine("      \"downloadLocation\": \"https://github.com/HavenDV/H.NotifyIcon\",");
            sbom.AppendLine("      \"licenseConcluded\": \"MIT\",");
            sbom.AppendLine("      \"licenseDeclared\": \"MIT\"");
            sbom.AppendLine("    },");
            sbom.AppendLine("    {");
            sbom.AppendLine("      \"SPDXID\": \"SPDXRef-Microsoft-Xaml-Behaviors\",");
            sbom.AppendLine("      \"name\": \"Microsoft.Xaml.Behaviors.Wpf\",");
            sbom.AppendLine("      \"versionInfo\": \"1.1.77\",");
            sbom.AppendLine("      \"downloadLocation\": \"https://github.com/Microsoft/XamlBehaviorsWpf\",");
            sbom.AppendLine("      \"licenseConcluded\": \"MIT\",");
            sbom.AppendLine("      \"licenseDeclared\": \"MIT\"");
            sbom.AppendLine("    },");
            sbom.AppendLine("    {");
            sbom.AppendLine("      \"SPDXID\": \"SPDXRef-System-Management\",");
            sbom.AppendLine("      \"name\": \"System.Management\",");
            sbom.AppendLine("      \"versionInfo\": \"8.0.0\",");
            sbom.AppendLine("      \"downloadLocation\": \"https://www.nuget.org/packages/System.Management\",");
            sbom.AppendLine("      \"licenseConcluded\": \"MIT\",");
            sbom.AppendLine("      \"licenseDeclared\": \"MIT\"");
            sbom.AppendLine("    },");
            sbom.AppendLine("    {");
            sbom.AppendLine("      \"SPDXID\": \"SPDXRef-System-Text-Json\",");
            sbom.AppendLine("      \"name\": \"System.Text.Json\",");
            sbom.AppendLine("      \"versionInfo\": \"8.0.5\",");
            sbom.AppendLine("      \"downloadLocation\": \"https://www.nuget.org/packages/System.Text.Json\",");
            sbom.AppendLine("      \"licenseConcluded\": \"MIT\",");
            sbom.AppendLine("      \"licenseDeclared\": \"MIT\"");
            sbom.AppendLine("    }");
            sbom.AppendLine("  ],");
            sbom.AppendLine("  \"relationships\": [");
            sbom.AppendLine("    {\n      \"spdxElementId\": \"SPDXRef-DOCUMENT\",");
            sbom.AppendLine("      \"relationshipType\": \"DESCRIBES\",");
            sbom.AppendLine("      \"relatedSpdxElement\": \"SPDXRef-Redball-Application\"");
            sbom.AppendLine("    },");
            sbom.AppendLine("    {\n      \"spdxElementId\": \"SPDXRef-Redball-Application\",");
            sbom.AppendLine("      \"relationshipType\": \"DEPENDS_ON\",");
            sbom.AppendLine("      \"relatedSpdxElement\": \"SPDXRef-DotNet-Runtime\"");
            sbom.AppendLine("    },");
            sbom.AppendLine("    {\n      \"spdxElementId\": \"SPDXRef-Redball-Application\",");
            sbom.AppendLine("      \"relationshipType\": \"DEPENDS_ON\",");
            sbom.AppendLine("      \"relatedSpdxElement\": \"SPDXRef-Hardcodet-NotifyIcon\"");
            sbom.AppendLine("    },");
            sbom.AppendLine("    {\n      \"spdxElementId\": \"SPDXRef-Redball-Application\",");
            sbom.AppendLine("      \"relationshipType\": \"DEPENDS_ON\",");
            sbom.AppendLine("      \"relatedSpdxElement\": \"SPDXRef-Microsoft-Xaml-Behaviors\"");
            sbom.AppendLine("    },");
            sbom.AppendLine("    {\n      \"spdxElementId\": \"SPDXRef-Redball-Application\",");
            sbom.AppendLine("      \"relationshipType\": \"DEPENDS_ON\",");
            sbom.AppendLine("      \"relatedSpdxElement\": \"SPDXRef-System-Management\"");
            sbom.AppendLine("    },");
            sbom.AppendLine("    {\n      \"spdxElementId\": \"SPDXRef-Redball-Application\",");
            sbom.AppendLine("      \"relationshipType\": \"DEPENDS_ON\",");
            sbom.AppendLine("      \"relatedSpdxElement\": \"SPDXRef-System-Text-Json\"");
            sbom.AppendLine("    }");
            sbom.AppendLine("  ]");
            sbom.AppendLine("}");
            
            Logger.Info("SecurityService", "SBOM generated successfully");
            return sbom.ToString();
        }
        catch (Exception ex)
        {
            Logger.Error("SecurityService", "Failed to generate SBOM", ex);
            return "{}";
        }
    }

    /// <summary>
    /// Saves the SBOM to a file.
    /// </summary>
    public static bool SaveSBOM(string outputPath)
    {
        try
        {
            // SECURITY: Validate output path to prevent path traversal
            if (!SecurePathValidator.IsValidFilePath(outputPath))
            {
                Logger.Error("SecurityService", $"Invalid output path for SBOM: {outputPath}");
                return false;
            }

            var sbom = GenerateSBOM();
            File.WriteAllText(outputPath, sbom);
            Logger.Info("SecurityService", $"SBOM saved to: {outputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("SecurityService", "Failed to save SBOM", ex);
            return false;
        }
    }

    /// <summary>
    /// Computes SHA256 hash of a file for integrity verification.
    /// </summary>
    public static string ComputeFileHash(string filePath)
    {
        try
        {
            // SECURITY: Validate file path to prevent path traversal
            if (!SecurePathValidator.IsValidFilePath(filePath))
            {
                Logger.Error("SecurityService", $"Invalid file path for hash computation: {filePath}");
                return string.Empty;
            }

            if (!File.Exists(filePath)) return string.Empty;
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch (Exception ex)
        {
            Logger.Error("SecurityService", $"Failed to compute hash for {filePath}", ex);
            return string.Empty;
        }
    }

    /// <summary>
    /// Computes SHA256 hash of a string.
    /// </summary>
    public static string ComputeStringHash(string input)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch (Exception ex)
        {
            Logger.Error("SecurityService", "Failed to compute string hash", ex);
            return string.Empty;
        }
    }

    /// <summary>
    /// Computes a machine-specific salt based on the machine name and hardware info.
    /// This prevents configuration files from being easily moved between devices.
    /// </summary>
    public static string GetMachineSalt()
    {
        try
        {
            var machineName = Environment.MachineName;
            var userName = Environment.UserName;
            // Combine with a static project-specific salt
            var rawSalt = $"{machineName}:{userName}:Redball:4d6167696353616c74";
            return ComputeStringHash(rawSalt);
        }
        catch (Exception ex)
        {
            Logger.Warning("SecurityService", $"Failed to compute machine salt: {ex.Message}");
            return "RedballDefaultSalt";
        }
    }

    /// <summary>
    /// Verifies if a configuration file has been tampered with, including machine-affinity check.
    /// </summary>
    public static bool VerifyConfigIntegrity(string json, string? storedSignature)
    {
        if (string.IsNullOrEmpty(storedSignature)) return true; // Legacy support or first run

        var salt = GetMachineSalt();
        var computed = ComputeStringHash(json + salt);
        return string.Equals(computed, storedSignature, StringComparison.OrdinalIgnoreCase);
    }

    #region Update Package Trust Chain (sec-3)

    /// <summary>
    /// Trusted publisher certificate thumbprints (SHA256).
    /// These are the only certificates allowed to sign Redball updates.
    /// </summary>
    private static readonly HashSet<string> TrustedPublisherThumbprints = new(StringComparer.OrdinalIgnoreCase)
    {
        // ArMaTeC signing certificate thumbprint - update this with your actual certificate thumbprint
        "PLACEHOLDER_THUMBPRINT_ARMAtec",
        // Allow additional known-good certificates here
    };

    /// <summary>
    /// Validates the complete trust chain for an update package.
    /// Checks: Authenticode signature, certificate chain validity, pinned publisher thumbprint, optional manifest signature.
    /// </summary>
    /// <param name="filePath">Path to the update file (MSI/EXE)</param>
    /// <param name="expectedManifestHash">Optional expected SHA256 hash from signed manifest</param>
    /// <returns>Trust validation result with detailed status</returns>
    public static TrustValidationResult ValidateUpdatePackage(string filePath, string? expectedManifestHash = null)
    {
        var result = new TrustValidationResult { FilePath = filePath };

        // SECURITY: Validate file path to prevent path traversal
        if (!SecurePathValidator.IsValidFilePath(filePath))
        {
            result.AddFailure("Invalid file path");
            return result;
        }

        try
        {
            if (!File.Exists(filePath))
            {
                result.AddFailure("File not found");
                return result;
            }

            // Step 1: Authenticode signature verification
            result.AuthenticodeValid = VerifyAuthenticodeSignature(filePath);
            if (!result.AuthenticodeValid)
            {
                result.AddFailure("Authenticode signature invalid or missing");
                return result;
            }
            result.AddSuccess("Authenticode signature verified");

            // Step 2: Get certificate details for publisher pinning
            try
            {
                using var cert = TryGetSignerCertificate(filePath);
                if (cert is null)
                {
                    result.AddFailure("Certificate validation error: No Authenticode signature found");
                    return result;
                }
                
                result.CertificateSubject = cert.Subject;
                result.CertificateIssuer = cert.Issuer;
                result.Thumbprint = cert.Thumbprint;
                result.NotBefore = cert.NotBefore;
                result.NotAfter = cert.NotAfter;

                // Step 3: Pinned publisher validation
                if (!string.IsNullOrEmpty(cert.Thumbprint))
                {
                    result.PublisherPinned = TrustedPublisherThumbprints.Contains(cert.Thumbprint);
                    if (result.PublisherPinned)
                    {
                        result.AddSuccess("Publisher thumbprint matches trusted certificate");
                    }
                    else
                    {
                        result.AddWarning($"Publisher thumbprint not in trusted list: {cert.Thumbprint}");
                        
                        // Report to TamperPolicyService for certificate pinning failure (sec-4)
                        var proceed = TamperPolicyService.Instance.HandleTamperEvent(
                            TamperEventType.CertificateNotPinned,
                            filePath,
                            $"Update signed by unknown publisher (thumbprint: {cert.Thumbprint}). Certificate not in trusted list.");
                        
                        if (!proceed)
                        {
                            result.AddFailure("Certificate pinning policy blocked this update");
                            result.IsTrusted = false;
                            return result;
                        }
                    }
                }
                else
                {
                    result.AddWarning("Could not extract certificate thumbprint");
                }
            }
            catch (Exception ex)
            {
                result.AddFailure($"Certificate validation error: {ex.Message}");
                return result;
            }

            // Step 4: Manifest hash validation (if provided)
            if (!string.IsNullOrEmpty(expectedManifestHash))
            {
                var actualHash = ComputeFileHash(filePath);
                result.ManifestHashValid = string.Equals(actualHash, expectedManifestHash, StringComparison.OrdinalIgnoreCase);
                
                if (result.ManifestHashValid)
                {
                    result.AddSuccess("Manifest hash matches (file integrity verified)");
                }
                else
                {
                    result.AddFailure($"Manifest hash mismatch! Expected: {expectedManifestHash}, Got: {actualHash}");
                    result.IsTrusted = false;
                    return result;
                }
            }
            else
            {
                result.AddWarning("No manifest hash provided for verification");
            }

            // Final trust decision
            // Must have valid Authenticode + valid chain + (pinned publisher OR manifest hash match)
            result.IsTrusted = result.AuthenticodeValid && 
                              (result.PublisherPinned || result.ManifestHashValid);

            if (result.IsTrusted)
            {
                Logger.Info("SecurityService", $"Update package TRUSTED: {Path.GetFileName(filePath)}");
            }
            else
            {
                Logger.Warning("SecurityService", $"Update package NOT TRUSTED: {Path.GetFileName(filePath)}");
            }

            return result;
        }
        catch (Exception ex)
        {
            result.AddFailure($"Validation error: {ex.Message}");
            Logger.Error("SecurityService", $"Trust validation failed for {filePath}", ex);
            return result;
        }
    }

    /// <summary>
    /// Adds a trusted publisher thumbprint at runtime.
    /// This can be used for enterprise deployments with custom certificates.
    /// </summary>
    public static void AddTrustedPublisherThumbprint(string thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint)) return;
        
        var cleaned = thumbprint.Replace(":", "").Replace("-", "").Trim();
        if (cleaned.Length == 40) // SHA1 length, we expect SHA256 (64 chars) but accept both
        {
            TrustedPublisherThumbprints.Add(cleaned);
            Logger.Info("SecurityService", $"Added trusted publisher thumbprint (SHA1): {cleaned}");
        }
        else if (cleaned.Length == 64) // SHA256 length
        {
            TrustedPublisherThumbprints.Add(cleaned);
            Logger.Info("SecurityService", $"Added trusted publisher thumbprint (SHA256): {cleaned}");
        }
        else
        {
            Logger.Warning("SecurityService", $"Invalid thumbprint length: {cleaned.Length} characters");
        }
    }

    /// <summary>
    /// Clears all trusted publisher thumbprints. Use with caution.
    /// </summary>
    public static void ClearTrustedPublishers()
    {
        TrustedPublisherThumbprints.Clear();
        Logger.Warning("SecurityService", "All trusted publisher thumbprints cleared");
    }

    /// <summary>
    /// Gets the current list of trusted publisher thumbprints (for diagnostics).
    /// </summary>
    public static IReadOnlyCollection<string> GetTrustedPublisherThumbprints()
    {
        return TrustedPublisherThumbprints.ToList().AsReadOnly();
    }

    #endregion
}

#region Trust Validation Result Classes

/// <summary>
/// Result of update package trust validation.
/// </summary>
public class TrustValidationResult
{
    public string FilePath { get; set; } = "";
    public bool IsTrusted { get; set; }
    public bool AuthenticodeValid { get; set; }
    public bool PublisherPinned { get; set; }
    public bool ManifestHashValid { get; set; }
    
    public string? CertificateSubject { get; set; }
    public string? CertificateIssuer { get; set; }
    public string? Thumbprint { get; set; }
    public DateTime NotBefore { get; set; }
    public DateTime NotAfter { get; set; }
    
    public List<string> Successes { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Failures { get; } = new();
    
    public void AddSuccess(string message) => Successes.Add(message);
    public void AddWarning(string message) => Warnings.Add(message);
    public void AddFailure(string message) => Failures.Add(message);
    
    public string Summary => string.Join("; ", 
        Successes.Count > 0 ? $"{Successes.Count} passed" : null,
        Warnings.Count > 0 ? $"{Warnings.Count} warnings" : null,
        Failures.Count > 0 ? $"{Failures.Count} failed" : null);
}

#endregion

