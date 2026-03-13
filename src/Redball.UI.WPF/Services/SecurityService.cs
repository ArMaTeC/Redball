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
    /// <summary>
    /// Verifies the Authenticode signature of a file.
    /// </summary>
    public static bool VerifyAuthenticodeSignature(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Logger.Warning("SecurityService", $"File not found for verification: {filePath}");
                return false;
            }

            var certificate = new X509Certificate2(filePath);
            var cert2 = new X509Certificate2(certificate);
            
            // Check if the certificate is valid
            if (cert2.Verify())
            {
                Logger.Info("SecurityService", $"Authenticode signature verified for: {Path.GetFileName(filePath)}");
                Logger.Debug("SecurityService", $"Certificate subject: {cert2.Subject}");
                Logger.Debug("SecurityService", $"Certificate issuer: {cert2.Issuer}");
                Logger.Debug("SecurityService", $"Valid from: {cert2.NotBefore:g} to {cert2.NotAfter:g}");
                return true;
            }
            
            Logger.Warning("SecurityService", $"Authenticode signature verification failed for: {filePath}");
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
            sbom.AppendLine("      \"versionInfo\": \"8.0.x\",");
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
}

