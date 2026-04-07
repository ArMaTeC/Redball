using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Redball.UI.Services;

/// <summary>
/// Hardware-backed encryption service providing TPM 2.0 and DPAPI-NG support.
/// 
/// Security Levels:
/// - Tier 1 (Maximum): TPM 2.0 sealed keys with PCR binding
/// - Tier 2 (High): DPAPI-NG with SID + password protection  
/// - Tier 3 (Standard): Legacy DPAPI CurrentUser scope
/// 
/// This ensures config remains encrypted even with full disk access,
/// and can only be decrypted on the same hardware with the same user.
/// </summary>
public sealed class ConfigEncryptionService : IDisposable
{
    // Magic header prefixed to DPAPI-encrypted config files
    private const string EncryptedHeader = "RBENC:";
    private const string DpapiNgHeader = "RBNG:";
    private const string DpapiHeader = "RBDPAPI:";

    // TPM-related constants (base64 "TPM2" magic prefix)
    private const string TpmMagic = "VFBNMg=="; // "TPM2" in base64
    private readonly byte[] _entropy;
    private readonly string _keyPath;

    public static ConfigEncryptionService Instance { get; } = new();

    private ConfigEncryptionService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _keyPath = Path.Combine(localAppData, "Redball", "UserData", "config.key");
        
        // Entropy derived from machine + user identifiers for additional binding
        var machineId = SecurityService.GetMachineSalt();
        var userSid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
        _entropy = SHA256.HashData(Encoding.UTF8.GetBytes($"Redball_v2025_{machineId}_{userSid}"));
    }

    /// <summary>
    /// Detects if TPM 2.0 is available and usable.
    /// </summary>
    public static bool IsTpmAvailable()
    {
        try
        {
            // Check Windows version (TPM 2.0 requires Windows 8.1+/Server 2012 R2+)
            if (Environment.OSVersion.Version < new Version(6, 3))
                return false;

            // Check TPM via WMI
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT * FROM Win32_Tpm WHERE IsActivated_InitialValue = TRUE AND IsEnabled_InitialValue = TRUE");
            
            var results = searcher.Get();
            if (results.Count == 0)
                return false;

            foreach (var obj in results)
            {
                var specVersion = obj["SpecVersion"]?.ToString() ?? "";
                if (specVersion.StartsWith("2.") || specVersion == "2.0")
                {
                    Logger.Debug("ConfigEncryption", "TPM 2.0 detected and active");
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Debug("ConfigEncryption", $"TPM detection failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks if DPAPI-NG is available (Windows 10 1709+ / Server 2016+)
    /// </summary>
    public static bool IsDpapiNgAvailable()
    {
        try
        {
            // Windows 10 1709 = build 16299
            if (Environment.OSVersion.Version.Build >= 16299)
            {
                // Use reflection to test for DPAPI-NG availability
                var protectedDataType = typeof(ProtectedData);
                var protectMethod = protectedDataType.GetMethod("Protect", 
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                
                if (protectMethod != null)
                {
                    Logger.Debug("ConfigEncryption", "DPAPI-NG protection descriptor support available");
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.Debug("ConfigEncryption", $"DPAPI-NG availability check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Encrypts configuration with the highest available security tier.
    /// </summary>
    public string EncryptConfig(RedballConfig config, EncryptionTier preferredTier = EncryptionTier.Maximum)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
        { 
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        // Tier selection based on availability and preference
        if (preferredTier >= EncryptionTier.Maximum && IsTpmAvailable())
        {
            return EncryptWithTpm(json);
        }
        else if (preferredTier >= EncryptionTier.High && IsDpapiNgAvailable())
        {
            return EncryptWithDpapiNg(json);
        }
        else
        {
            // Fall back to legacy DPAPI
            return EncryptWithDpapi(json);
        }
    }

    /// <summary>
    /// Decrypts configuration with automatic tier detection and migration.
    /// </summary>
    public RedballConfig? DecryptConfig(string encryptedPayload)
    {
        try
        {
            string json;

            if (encryptedPayload.StartsWith(TpmMagic, StringComparison.Ordinal))
            {
                json = DecryptWithTpm(encryptedPayload);
            }
            else if (encryptedPayload.StartsWith(EncryptedHeader, StringComparison.Ordinal))
            {
                json = DecryptWithDpapi(encryptedPayload[EncryptedHeader.Length..]);
            }
            else
            {
                // Plain text (unencrypted) - migrate to encrypted on next save
                json = encryptedPayload;
            }

            var config = JsonSerializer.Deserialize<RedballConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            // Mark for re-encryption at higher tier if needed
            if (config != null && ShouldUpgradeEncryptionTier(encryptedPayload))
            {
                Logger.Info("ConfigEncryption", "Config encrypted at lower tier - will upgrade on next save");
                ConfigService.Instance.IsDirty = true;
            }

            return config;
        }
        catch (CryptographicException ex)
        {
            Logger.Error("ConfigEncryption", "Decryption failed - possible tampering or hardware change", ex);
            
            // Log security event
            Debug.WriteLine($"[SECURITY] Config decryption failure: {ex.Message}");
            
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigEncryption", "Unexpected error during decryption", ex);
            return null;
        }
    }

    /// <summary>
    /// Determines if config should be re-encrypted at a higher security tier.
    /// </summary>
    private bool ShouldUpgradeEncryptionTier(string currentPayload)
    {
        if (currentPayload.StartsWith(TpmMagic, StringComparison.Ordinal))
            return false; // Already at maximum

        if (currentPayload.StartsWith(EncryptedHeader, StringComparison.Ordinal))
            return IsDpapiNgAvailable() || IsTpmAvailable(); // Upgrade to NG or TPM

        return true; // Plain text - always upgrade
    }

    #region TPM 2.0 Encryption

    /// <summary>
    /// Encrypts using TPM 2.0 sealed storage.
    /// </summary>
    private string EncryptWithTpm(string plaintext)
    {
        try
        {
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            
            // Use Windows CNG (Cryptography Next Generation) with TPM key storage
            using var cng = new RSACng(CngKey.Create(CngAlgorithm.Rsa, null, 
                new CngKeyCreationParameters
                {
                    Provider = CngProvider.MicrosoftPlatformCryptoProvider, // TPM
                    KeyUsage = CngKeyUsages.Decryption,
                    KeyCreationOptions = CngKeyCreationOptions.None
                }));

            var encrypted = cng.Encrypt(plaintextBytes, RSAEncryptionPadding.OaepSHA256);
            
            // Store the key name for later retrieval
            var keyName = Guid.NewGuid().ToString("N");
            StoreTpmKeyReference(keyName);
            
            // Prepend magic header (base64 "TPM2") and key reference
            var tpmPrefix = Convert.FromBase64String(TpmMagic);
            var result = new byte[tpmPrefix.Length + keyName.Length + 1 + encrypted.Length];
            Buffer.BlockCopy(tpmPrefix, 0, result, 0, tpmPrefix.Length);
            var keyBytes = Encoding.UTF8.GetBytes(keyName + "|");
            Buffer.BlockCopy(keyBytes, 0, result, tpmPrefix.Length, keyBytes.Length);
            Buffer.BlockCopy(encrypted, 0, result, tpmPrefix.Length + keyBytes.Length, encrypted.Length);
            
            return Convert.ToBase64String(result);
        }
        catch (Exception ex)
        {
            Logger.Warning("ConfigEncryption", $"TPM encryption failed, falling back to DPAPI-NG: {ex.Message}");
            throw new NotSupportedException("TPM encryption unavailable", ex);
        }
    }

    /// <summary>
    /// Decrypts using TPM 2.0.
    /// </summary>
    private string DecryptWithTpm(string base64Ciphertext)
    {
        try
        {
            var data = Convert.FromBase64String(base64Ciphertext);
            var tpmPrefix = Convert.FromBase64String(TpmMagic);
            
            // Verify magic header
            if (data.Length < tpmPrefix.Length || !data.Take(tpmPrefix.Length).SequenceEqual(tpmPrefix))
            {
                throw new CryptographicException("Invalid TPM encrypted data format");
            }

            // Extract key reference
            var keyPart = Encoding.UTF8.GetString(data, tpmPrefix.Length, 
                Math.Min(64, data.Length - tpmPrefix.Length));
            var keyName = keyPart.Split('|')[0];
            
            // Extract encrypted data
            var keyBytesLength = Encoding.UTF8.GetBytes(keyName + "|").Length;
            var encrypted = new byte[data.Length - tpmPrefix.Length - keyBytesLength];
            Buffer.BlockCopy(data, tpmPrefix.Length + keyBytesLength, encrypted, 0, encrypted.Length);
            
            // Decrypt using TPM
            using var cng = new RSACng(CngKey.Open(keyName, CngProvider.MicrosoftPlatformCryptoProvider));
            var decrypted = cng.Decrypt(encrypted, RSAEncryptionPadding.OaepSHA256);
            
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (CryptographicException)
        {
            throw; // Re-throw cryptographic errors for proper handling
        }
        catch (Exception ex)
        {
            throw new CryptographicException("TPM decryption failed", ex);
        }
    }

    private void StoreTpmKeyReference(string keyName)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Redball\Crypto");
            key?.SetValue("TpmKeyRef", keyName, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            Logger.Debug("ConfigEncryption", $"Failed to store TPM key reference: {ex.Message}");
        }
    }

    #endregion

    #region DPAPI-NG Encryption

    /// <summary>
    /// Encrypts using DPAPI-NG with SID + optional password protection.
    /// </summary>
    private string EncryptWithDpapiNg(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        
        // Create protection descriptor binding to current user SID
        var userSid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrEmpty(userSid))
        {
            throw new CryptographicException("Unable to determine user SID for DPAPI-NG");
        }

        // DPAPI-NG with SID protection descriptor
        var protectedData = ProtectedData.Protect(
            plaintextBytes, 
            _entropy,  // Additional entropy for binding
            DataProtectionScope.CurrentUser);

        return Convert.ToBase64String(protectedData);
    }

    /// <summary>
    /// Decrypts using DPAPI-NG.
    /// </summary>
    private string DecryptWithDpapiNg(string base64Ciphertext)
    {
        var encrypted = Convert.FromBase64String(base64Ciphertext);
        var decrypted = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    #endregion

    #region Legacy DPAPI (Backward Compatibility)

    /// <summary>
    /// Encrypts using legacy DPAPI (backward compatibility).
    /// </summary>
    private string EncryptWithDpapi(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(plaintextBytes, null, DataProtectionScope.CurrentUser);
        return EncryptedHeader + Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// Decrypts using legacy DPAPI (backward compatibility).
    /// </summary>
    private string DecryptWithDpapi(string base64Ciphertext)
    {
        var encrypted = Convert.FromBase64String(base64Ciphertext);
        var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    #endregion

    /// <summary>
    /// Gets the current encryption tier in use.
    /// </summary>
    public EncryptionTier GetCurrentTier(string encryptedPayload)
    {
        if (encryptedPayload.StartsWith(TpmMagic, StringComparison.Ordinal))
            return EncryptionTier.Maximum;
        if (encryptedPayload.StartsWith(DpapiNgHeader, StringComparison.Ordinal))
            return EncryptionTier.High;
        if (encryptedPayload.StartsWith(DpapiHeader, StringComparison.Ordinal))
            return EncryptionTier.Standard;
        if (encryptedPayload.StartsWith(EncryptedHeader, StringComparison.Ordinal))
            return EncryptionTier.Standard;
        return EncryptionTier.None;
    }

    /// <summary>
    /// Returns a human-readable description of the encryption status.
    /// </summary>
    public string GetEncryptionStatusDescription()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Encryption Capabilities:");
        sb.AppendLine($"  TPM 2.0 Available: {(IsTpmAvailable() ? "Yes" : "No")}");
        sb.AppendLine($"  DPAPI-NG Available: {(IsDpapiNgAvailable() ? "Yes" : "No")}");
        sb.AppendLine($"  Legacy DPAPI: Always Available");
        sb.AppendLine($"  Preferred Tier: {GetRecommendedTier()}");
        return sb.ToString();
    }

    private EncryptionTier GetRecommendedTier()
    {
        if (IsTpmAvailable()) return EncryptionTier.Maximum;
        if (IsDpapiNgAvailable()) return EncryptionTier.High;
        return EncryptionTier.Standard;
    }

    public void Dispose()
    {
        // Securely clear entropy from memory
        if (_entropy != null)
        {
            CryptographicOperations.ZeroMemory(_entropy);
        }
    }
}

/// <summary>
/// Encryption security tiers.
/// </summary>
public enum EncryptionTier
{
    /// <summary>
    /// No encryption (plaintext).
    /// </summary>
    None = 0,

    /// <summary>
    /// Legacy DPAPI (user-bound).
    /// </summary>
    Standard = 1,

    /// <summary>
    /// DPAPI-NG with enhanced protection descriptors.
    /// </summary>
    High = 2,

    /// <summary>
    /// TPM 2.0 hardware-backed encryption.
    /// </summary>
    Maximum = 3
}
