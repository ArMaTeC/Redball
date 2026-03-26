// Copyright (c) ArMaTeC. All rights reserved.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Windows Credential Manager-based secret provider.
/// Stores secrets in the Windows Credential Store using CredWrite/CredRead APIs.
/// Secrets are encrypted by Windows using the user's login credentials.
/// </summary>
public sealed class WindowsCredentialSecretProvider : ISecretProvider, IDisposable
{
    private readonly string _targetPrefix;
    private readonly object _lock = new();
    private bool _disposed;

    public WindowsCredentialSecretProvider(string targetPrefix = "Redball")
    {
        _targetPrefix = targetPrefix;
    }

    public string ProviderName => "Windows Credential Manager";
    
    public bool IsAvailable => OperatingSystem.IsWindows();

    public Task StoreAsync(string key, string value, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsCredentialSecretProvider));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));
        if (value == null) throw new ArgumentNullException(nameof(value));

        lock (_lock)
        {
            try
            {
                var targetName = BuildTargetName(key);
                var byteArray = Encoding.UTF8.GetBytes(value);

                // Delete existing credential first to avoid duplicates
                DeleteCredential(targetName);

                var credential = new CREDENTIAL
                {
                    Type = CRED_TYPE.GENERIC,
                    TargetName = Marshal.StringToCoTaskMemUni(targetName),
                    CredentialBlob = Marshal.AllocCoTaskMem(byteArray.Length),
                    CredentialBlobSize = byteArray.Length,
                    Persist = (uint)CRED_PERSIST.LOCAL_MACHINE,
                    UserName = Marshal.StringToCoTaskMemUni(Environment.UserName)
                };

                try
                {
                    Marshal.Copy(byteArray, 0, credential.CredentialBlob, byteArray.Length);

                    if (!CredWrite(ref credential, 0))
                    {
                        var error = Marshal.GetLastWin32Error();
                        throw new Win32Exception(error, $"Failed to write credential for key '{key}'");
                    }

                    Logger.Info("WindowsCredentialSecretProvider", $"Stored secret for key: {key}");
                }
                finally
                {
                    Marshal.FreeCoTaskMem(credential.TargetName);
                    Marshal.FreeCoTaskMem(credential.CredentialBlob);
                    Marshal.FreeCoTaskMem(credential.UserName);
                }

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Logger.Error("WindowsCredentialSecretProvider", $"Failed to store secret for key '{key}'", ex);
                throw;
            }
        }
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsCredentialSecretProvider));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

        lock (_lock)
        {
            try
            {
                var targetName = BuildTargetName(key);
                var credentialPtr = IntPtr.Zero;

                try
                {
                    if (!CredRead(targetName, CRED_TYPE.GENERIC, 0, out credentialPtr))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error == ERROR_NOT_FOUND)
                        {
                            Logger.Debug("WindowsCredentialSecretProvider", $"Secret not found for key: {key}");
                            return Task.FromResult<string?>(null);
                        }
                        throw new Win32Exception(error, $"Failed to read credential for key '{key}'");
                    }

                    var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
                    
                    if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0)
                    {
                        Logger.Warning("WindowsCredentialSecretProvider", $"Empty credential blob for key: {key}");
                        return Task.FromResult<string?>(null);
                    }

                    var byteArray = new byte[credential.CredentialBlobSize];
                    Marshal.Copy(credential.CredentialBlob, byteArray, 0, (int)credential.CredentialBlobSize);
                    var value = Encoding.UTF8.GetString(byteArray);

                    Logger.Debug("WindowsCredentialSecretProvider", $"Retrieved secret for key: {key}");
                    return Task.FromResult<string?>(value);
                }
                finally
                {
                    if (credentialPtr != IntPtr.Zero)
                    {
                        CredFree(credentialPtr);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("WindowsCredentialSecretProvider", $"Failed to retrieve secret for key '{key}'", ex);
                return Task.FromResult<string?>(null);
            }
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsCredentialSecretProvider));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

        lock (_lock)
        {
            try
            {
                var targetName = BuildTargetName(key);
                DeleteCredential(targetName);
                Logger.Info("WindowsCredentialSecretProvider", $"Deleted secret for key: {key}");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Logger.Error("WindowsCredentialSecretProvider", $"Failed to delete secret for key '{key}'", ex);
                throw;
            }
        }
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsCredentialSecretProvider));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

        lock (_lock)
        {
            try
            {
                var targetName = BuildTargetName(key);
                var credentialPtr = IntPtr.Zero;

                try
                {
                    var exists = CredRead(targetName, CRED_TYPE.GENERIC, 0, out credentialPtr);
                    if (exists && credentialPtr != IntPtr.Zero)
                    {
                        CredFree(credentialPtr);
                    }
                    return Task.FromResult(exists);
                }
                catch
                {
                    return Task.FromResult(false);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("WindowsCredentialSecretProvider", $"Failed to check existence for key '{key}'", ex);
                return Task.FromResult(false);
            }
        }
    }

    /// <summary>
    /// Lists all stored secret keys for this application.
    /// </summary>
    public string[] ListKeys()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsCredentialSecretProvider));

        lock (_lock)
        {
            var keys = new System.Collections.Generic.List<string>();
            var filter = $"{_targetPrefix}:";
            
            try
            {
                // Enumerate all credentials matching our prefix
                var count = 0;
                var credentialsPtr = IntPtr.Zero;

                if (CredEnumerate(filter, 0, out count, out credentialsPtr))
                {
                    try
                    {
                        for (var i = 0; i < count; i++)
                        {
                            var ptr = Marshal.ReadIntPtr(credentialsPtr, i * IntPtr.Size);
                            var credential = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                            
                            if (credential.TargetName != IntPtr.Zero)
                            {
                                var targetName = Marshal.PtrToStringUni(credential.TargetName);
                                if (targetName?.StartsWith(filter, StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    var key = targetName.Substring(filter.Length);
                                    keys.Add(key);
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (credentialsPtr != IntPtr.Zero)
                        {
                            CredFree(credentialsPtr);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("WindowsCredentialSecretProvider", $"Failed to enumerate credentials: {ex.Message}");
            }

            return keys.ToArray();
        }
    }

    private string BuildTargetName(string key)
    {
        return $"{_targetPrefix}:{key}";
    }

    private void DeleteCredential(string targetName)
    {
        try
        {
            CredDelete(targetName, CRED_TYPE.GENERIC, 0);
        }
        catch
        {
            // Ignore errors when deleting non-existent credentials
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Logger.Debug("WindowsCredentialSecretProvider", "Disposed");
        }
    }

    #region P/Invoke Declarations

    private const int ERROR_NOT_FOUND = 1168;

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, CRED_TYPE type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, CRED_TYPE type, int reservedFlag);

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr credentials);

    [DllImport("Advapi32.dll", SetLastError = false)]
    private static extern void CredFree([In] IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public CRED_TYPE Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    private enum CRED_TYPE : uint
    {
        GENERIC = 1,
        DOMAIN_PASSWORD = 2,
        DOMAIN_CERTIFICATE = 3,
        DOMAIN_VISIBLE_PASSWORD = 4,
        GENERIC_CERTIFICATE = 5,
        DOMAIN_EXTENDED = 6,
        MAXIMUM = 7
    }

    private enum CRED_PERSIST : uint
    {
        SESSION = 1,
        LOCAL_MACHINE = 2,
        ENTERPRISE = 3
    }

    #endregion
}
