// Copyright (c) ArMaTeC. All rights reserved.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Provides secure storage and retrieval of sensitive secrets using OS-protected mechanisms.
/// Secrets are never stored in plain text in configuration files.
/// </summary>
public interface ISecretProvider : IDisposable
{
    /// <summary>
    /// Stores a secret securely.
    /// </summary>
    /// <param name="key">The secret identifier/key name.</param>
    /// <param name="value">The secret value to store.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StoreAsync(string key, string value, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a secret securely.
    /// </summary>
    /// <param name="key">The secret identifier/key name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The secret value, or null if not found or access denied.</returns>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Deletes a stored secret.
    /// </summary>
    /// <param name="key">The secret identifier/key name.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Checks if a secret exists in secure storage.
    /// </summary>
    /// <param name="key">The secret identifier/key name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the secret exists, false otherwise.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Gets the provider name for diagnostics.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets whether the provider is available on this system.
    /// </summary>
    bool IsAvailable { get; }
}
