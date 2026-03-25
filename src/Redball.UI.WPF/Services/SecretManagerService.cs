// Copyright (c) ArMaTeC. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// High-level secret management service that provides secure storage
/// and automatic fallback mechanisms for sensitive application data.
/// This service ensures API keys and credentials are never stored in plain text configuration files.
/// </summary>
public sealed class SecretManagerService : IDisposable
{
    private readonly ISecretProvider _primaryProvider;
    private readonly ISecretProvider? _fallbackProvider;
    private readonly List<ISecretProvider> _providers;
    private bool _disposed;

    /// <summary>
    /// Well-known secret key names used throughout the application.
    /// </summary>
    public static class KnownSecrets
    {
        public const string CloudAnalyticsApiKey = "CloudAnalytics:ApiKey";
        public const string CloudAnalyticsEndpoint = "CloudAnalytics:Endpoint";
        public const string UpdatePublisherThumbprint = "Update:PublisherThumbprint";
        public const string WebApiAuthToken = "WebApi:AuthToken";
        public const string PluginRepositoryToken = "Plugin:RepositoryToken";
        public const string TelemetryEndpoint = "Telemetry:Endpoint";
    }

    public SecretManagerService(ISecretProvider primaryProvider, ISecretProvider? fallbackProvider = null)
    {
        _primaryProvider = primaryProvider ?? throw new ArgumentNullException(nameof(primaryProvider));
        _fallbackProvider = fallbackProvider;
        _providers = new List<ISecretProvider> { primaryProvider };
        if (fallbackProvider != null)
        {
            _providers.Add(fallbackProvider);
        }

        Logger.Info("SecretManagerService", $"Initialized with {(_providers.Count)} provider(s): {string.Join(", ", _providers.Select(p => p.ProviderName))}");
    }

    /// <summary>
    /// Stores a secret using the primary provider.
    /// If the primary fails and a fallback exists, attempts fallback storage.
    /// </summary>
    public async Task<bool> StoreSecretAsync(string key, string value, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SecretManagerService));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));
        if (string.IsNullOrEmpty(value)) throw new ArgumentException("Value cannot be null or empty", nameof(value));

        try
        {
            await _primaryProvider.StoreAsync(key, value, ct);
            Logger.Info("SecretManagerService", $"Secret stored successfully: {key}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("SecretManagerService", $"Primary provider failed to store secret '{key}'", ex);

            if (_fallbackProvider != null)
            {
                try
                {
                    await _fallbackProvider.StoreAsync(key, value, ct);
                    Logger.Warning("SecretManagerService", $"Secret '{key}' stored using fallback provider");
                    return true;
                }
                catch (Exception fallbackEx)
                {
                    Logger.Error("SecretManagerService", $"Fallback provider also failed for secret '{key}'", fallbackEx);
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Retrieves a secret from secure storage.
    /// Tries primary provider first, then fallback if configured.
    /// </summary>
    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SecretManagerService));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

        // Try primary provider first
        try
        {
            var value = await _primaryProvider.GetAsync(key, ct);
            if (value != null)
            {
                Logger.Debug("SecretManagerService", $"Secret retrieved from primary provider: {key}");
                return value;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("SecretManagerService", $"Primary provider failed to retrieve '{key}': {ex.Message}");
        }

        // Try fallback provider
        if (_fallbackProvider != null)
        {
            try
            {
                var value = await _fallbackProvider.GetAsync(key, ct);
                if (value != null)
                {
                    Logger.Debug("SecretManagerService", $"Secret retrieved from fallback provider: {key}");
                    return value;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("SecretManagerService", $"Fallback provider failed to retrieve '{key}': {ex.Message}");
            }
        }

        Logger.Debug("SecretManagerService", $"Secret not found: {key}");
        return null;
    }

    /// <summary>
    /// Deletes a secret from all providers.
    /// </summary>
    public async Task<bool> DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SecretManagerService));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var success = false;
        var errors = new List<string>();

        foreach (var provider in _providers)
        {
            try
            {
                if (await provider.ExistsAsync(key, ct))
                {
                    await provider.DeleteAsync(key, ct);
                    Logger.Info("SecretManagerService", $"Secret deleted from {provider.ProviderName}: {key}");
                    success = true;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{provider.ProviderName}: {ex.Message}");
            }
        }

        if (!success && errors.Count > 0)
        {
            Logger.Error("SecretManagerService", $"Failed to delete secret '{key}'. Errors: {string.Join("; ", errors)}");
        }

        return success;
    }

    /// <summary>
    /// Checks if a secret exists in secure storage.
    /// </summary>
    public async Task<bool> SecretExistsAsync(string key, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SecretManagerService));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

        foreach (var provider in _providers)
        {
            try
            {
                if (await provider.ExistsAsync(key, ct))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("SecretManagerService", $"Provider {provider.ProviderName} failed to check existence for '{key}': {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// Lists all stored secrets (keys only, not values).
    /// </summary>
    public async Task<string[]> ListSecretsAsync(CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SecretManagerService));

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_primaryProvider is WindowsCredentialSecretProvider winProvider)
        {
            foreach (var key in winProvider.ListKeys())
            {
                keys.Add(key);
            }
        }

        return keys.ToArray();
    }

    /// <summary>
    /// Migrates a secret from plain-text storage to secure storage.
    /// Used during configuration migration scenarios.
    /// </summary>
    public async Task<bool> MigrateSecretAsync(string key, string? plainValue, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SecretManagerService));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));
        if (string.IsNullOrEmpty(plainValue))
        {
            Logger.Debug("SecretManagerService", $"Skipping migration for '{key}' - no value to migrate");
            return false;
        }

        try
        {
            // Check if already in secure storage
            if (await SecretExistsAsync(key, ct))
            {
                Logger.Info("SecretManagerService", $"Secret '{key}' already exists in secure storage, skipping migration");
                return true;
            }

            // Store in secure storage
            var success = await StoreSecretAsync(key, plainValue, ct);
            if (success)
            {
                Logger.Info("SecretManagerService", $"Migrated secret '{key}' from plain-text to secure storage");
            }
            return success;
        }
        catch (Exception ex)
        {
            Logger.Error("SecretManagerService", $"Failed to migrate secret '{key}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets the health status of the secret storage providers.
    /// </summary>
    public SecretProviderHealth GetHealth()
    {
        return new SecretProviderHealth
        {
            PrimaryProvider = _primaryProvider.ProviderName,
            PrimaryAvailable = _primaryProvider.IsAvailable,
            FallbackProvider = _fallbackProvider?.ProviderName,
            FallbackAvailable = _fallbackProvider?.IsAvailable ?? false,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Singleton instance accessor for service locator pattern.
    /// </summary>
    public static SecretManagerService Instance { get; } = new SecretManagerService(
        new WindowsCredentialSecretProvider("Redball"),
        null // No fallback for now - we want secure storage or nothing
    );

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _primaryProvider?.Dispose();
            _fallbackProvider?.Dispose();
            Logger.Debug("SecretManagerService", "Disposed");
        }
    }
}

/// <summary>
/// Health status for secret providers.
/// </summary>
public class SecretProviderHealth
{
    public string PrimaryProvider { get; set; } = "";
    public bool PrimaryAvailable { get; set; }
    public string? FallbackProvider { get; set; }
    public bool FallbackAvailable { get; set; }
    public DateTime Timestamp { get; set; }
}
