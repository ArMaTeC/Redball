using System;
using System.Security.Cryptography;
using System.Text;

namespace Redball.UI.Services;

/// <summary>
/// Provides DPAPI integrated encryption for zero-trust local configuration storage.
/// Binds configuration data exclusively to the current Windows user.
/// </summary>
public static class SecureConfigProvider 
{
    public static string EncryptSecret(string plaintext) 
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;

        try
        {
            byte[] payload = Encoding.UTF8.GetBytes(plaintext);
            byte[] encrypted = ProtectedData.Protect(payload, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch (CryptographicException)
        {
            Logger.Error("SecureConfigProvider", "Failed to encrypt payload with DPAPI.");
            throw;
        }
    }
    
    public static string DecryptSecret(string ciphertext) 
    {
        if (string.IsNullOrEmpty(ciphertext)) return ciphertext;

        try
        {
            byte[] payload = Convert.FromBase64String(ciphertext);
            byte[] decrypted = ProtectedData.Unprotect(payload, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (CryptographicException)
        {
            Logger.Error("SecureConfigProvider", "Failed to decrypt payload with DPAPI.");
            throw;
        }
        catch (FormatException)
        {
            // Not base64
            throw;
        }
    }
}
