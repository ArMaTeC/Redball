using System;
using System.Runtime.InteropServices;
using System.Security;

namespace Redball.UI.Services;

/// <summary>
/// Security & Data Integrity (4.2): SecureString in-memory allocation for TypeThing payloads.
/// Protects memory scraping vectors when pulling large sensitive items (API keys/Passphrases) out of the Clipboard.
/// </summary>
public class SecureTypeThingPayload : IDisposable
{
    private SecureString _secureClipboardData;

    public SecureTypeThingPayload(string rawClipboardData)
    {
        _secureClipboardData = new SecureString();
        
        foreach (char c in rawClipboardData)
        {
            _secureClipboardData.AppendChar(c);
        }
        
        // Finalize the string memory locking it down in RAM
        _secureClipboardData.MakeReadOnly();
    }

    /// <summary>
    /// Temporarily unpacks the requested character natively for P/Invoke `SendInput` structures.
    /// This prevents allocating string replicas onto the LOH.
    /// </summary>
    public char GetCharacterAt(int index)
    {
        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.SecureStringToBSTR(_secureClipboardData);
            return (char)Marshal.ReadInt16(ptr, index * 2);
        }
        finally
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.ZeroFreeBSTR(ptr);
            }
        }
    }

    public int Length => _secureClipboardData.Length;

    public void Dispose()
    {
        _secureClipboardData?.Dispose();
        GC.SuppressFinalize(this);
    }
}
