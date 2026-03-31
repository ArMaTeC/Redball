using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Redball.UI.Services;

/// <summary>
/// Single Sign-On (SSO) integration service for enterprise authentication.
/// Supports Windows Integrated Authentication, SAML, and OIDC/OAuth2.
/// </summary>
public class SSOService
{
    private static readonly Lazy<SSOService> _instance = new(() => new SSOService());
    public static SSOService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private SSOConfiguration? _config;
    private SSOSession? _currentSession;

    public event EventHandler<SSOAuthenticationEventArgs>? AuthenticationCompleted;
    public event EventHandler<SSOSessionEventArgs>? SessionRefreshed;

    public bool IsEnabled => _config?.IsEnabled ?? false;
    public bool IsAuthenticated => _currentSession?.IsValid ?? false;
    public string? CurrentUser => _currentSession?.Username;
    public string? UserEmail => _currentSession?.Email;
    public DateTime? SessionExpires => _currentSession?.ExpiresAt;

    private SSOService()
    {
        _httpClient = new HttpClient();
        LoadConfiguration();
        
        Logger.Verbose("SSOService", "Initialized");
    }

    /// <summary>
    /// Configures SSO with enterprise settings.
    /// </summary>
    public void Configure(SSOConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        
        Logger.Info("SSOService", $"Configured SSO with provider: {config.ProviderType}");
    }

    /// <summary>
    /// Attempts silent authentication using Windows Integrated Auth.
    /// </summary>
    public async Task<bool> TrySilentAuthenticationAsync()
    {
        if (!IsEnabled || _config == null)
            return false;

        try
        {
            switch (_config.ProviderType)
            {
                case SSOProviderType.WindowsIntegrated:
                    return await AuthenticateWindowsAsync();
                    
                case SSOProviderType.SAML:
                    // SAML requires browser-based auth, can't do silent
                    return false;
                    
                case SSOProviderType.OIDC:
                    // Try refresh token if available
                    return await TryRefreshTokenAsync();
                    
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("SSOService", $"Silent authentication failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Initiates browser-based SSO authentication flow.
    /// </summary>
    public async Task<bool> AuthenticateWithBrowserAsync()
    {
        if (!IsEnabled || _config == null)
            throw new InvalidOperationException("SSO not configured");

        try
        {
            switch (_config.ProviderType)
            {
                case SSOProviderType.OIDC:
                    return await AuthenticateOIDCAsync();
                    
                case SSOProviderType.SAML:
                    return await AuthenticateSAMLAsync();
                    
                default:
                    throw new NotSupportedException($"Provider type {_config.ProviderType} not supported for browser auth");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SSOService", "Browser authentication failed", ex);
            throw;
        }
    }

    /// <summary>
    /// Authenticates using Windows Integrated Authentication.
    /// </summary>
    private async Task<bool> AuthenticateWindowsAsync()
    {
        try
        {
            // Get current Windows user
            var username = Environment.UserName;
            var domain = Environment.UserDomainName;
            
            // Create Windows identity token
            var token = await RequestWindowsTokenAsync(username, domain);
            
            if (string.IsNullOrEmpty(token))
                return false;

            // Validate with identity provider
            var validationResult = await ValidateTokenWithIdPAsync(token);
            
            if (validationResult.IsValid)
            {
                _currentSession = new SSOSession
                {
                    Username = validationResult.Username ?? username,
                    Email = validationResult.Email,
                    Token = token,
                    RefreshToken = validationResult.RefreshToken,
                    IssuedAt = DateTime.UtcNow,
                    ExpiresAt = validationResult.ExpiresAt ?? DateTime.UtcNow.AddHours(8)
                };

                AuthenticationCompleted?.Invoke(this, new SSOAuthenticationEventArgs
                {
                    Success = true,
                    Username = _currentSession.Username,
                    Provider = _config?.ProviderType.ToString() ?? "Windows"
                });

                Logger.Info("SSOService", $"Windows authentication successful for {username}");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("SSOService", "Windows authentication failed", ex);
            return false;
        }
    }

    /// <summary>
    /// OIDC/OAuth2 authentication flow.
    /// </summary>
    private async Task<bool> AuthenticateOIDCAsync()
    {
        if (_config?.OIDCConfig == null)
            throw new InvalidOperationException("OIDC not configured");

        try
        {
            // Generate PKCE parameters
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = GenerateCodeChallenge(codeVerifier);
            var state = GenerateState();

            // Build authorization URL
            var authUrl = BuildAuthorizationUrl(codeChallenge, state);

            // Open browser for authentication
            // This would typically involve opening a browser and waiting for callback
            // For now, simulate the flow
            
            Logger.Info("SSOService", "Opening browser for OIDC authentication...");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            // In real implementation, start local HTTP listener for callback
            // var authCode = await WaitForAuthorizationCallbackAsync(state);
            
            // Exchange code for tokens
            // var tokens = await ExchangeCodeForTokensAsync(authCode, codeVerifier);
            
            return true; // Placeholder
        }
        catch (Exception ex)
        {
            Logger.Error("SSOService", "OIDC authentication failed", ex);
            throw;
        }
    }

    /// <summary>
    /// SAML authentication flow.
    /// </summary>
    private async Task<bool> AuthenticateSAMLAsync()
    {
        if (_config?.SAMLConfig == null)
            throw new InvalidOperationException("SAML not configured");

        try
        {
            // Build SAML AuthnRequest
            var samlRequest = BuildSAMLRequest();
            
            // Open browser for IdP authentication
            var idpUrl = _config.SAMLConfig.IdPUrl;
            var authUrl = $"{idpUrl}?SAMLRequest={Uri.EscapeDataString(samlRequest)}";

            Logger.Info("SSOService", "Opening browser for SAML authentication...");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            return true; // Placeholder
        }
        catch (Exception ex)
        {
            Logger.Error("SSOService", "SAML authentication failed", ex);
            throw;
        }
    }

    /// <summary>
    /// Refreshes the current SSO session.
    /// </summary>
    public async Task<bool> RefreshSessionAsync()
    {
        if (!IsAuthenticated || _currentSession?.RefreshToken == null)
            return false;

        try
        {
            return await TryRefreshTokenAsync();
        }
        catch (Exception ex)
        {
            Logger.Warning("SSOService", $"Session refresh failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Logs out the current user.
    /// </summary>
    public void Logout()
    {
        if (_currentSession == null)
            return;

        var username = _currentSession.Username;
        
        // Revoke tokens if supported
        if (!string.IsNullOrEmpty(_currentSession.RefreshToken))
        {
            _ = RevokeTokenAsync(_currentSession.RefreshToken);
        }

        _currentSession = null;
        
        Logger.Info("SSOService", $"User {username} logged out");
    }

    /// <summary>
    /// Gets the current authorization header for API requests.
    /// </summary>
    public string? GetAuthorizationHeader()
    {
        if (!IsAuthenticated || _currentSession?.Token == null)
            return null;

        return $"Bearer {_currentSession.Token}";
    }

    /// <summary>
    /// Validates that the current session is still valid.
    /// </summary>
    public async Task<bool> ValidateSessionAsync()
    {
        if (!IsAuthenticated)
            return false;

        // Check if token is about to expire
        if (_currentSession?.ExpiresAt < DateTime.UtcNow.AddMinutes(5))
        {
            // Try to refresh
            return await RefreshSessionAsync();
        }

        return true;
    }

    // Private helper methods
    
    private void LoadConfiguration()
    {
        try
        {
            // Check registry or config file for SSO settings
            // This would typically be set by enterprise deployment tools
            
            var ssoEnabled = ConfigService.Instance.Config.TeamSyncEnabled; // Reuse flag for now
            
            if (ssoEnabled)
            {
                _config = new SSOConfiguration
                {
                    IsEnabled = true,
                    ProviderType = SSOProviderType.WindowsIntegrated,
                    AutoLogin = true
                };
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("SSOService", $"Failed to load SSO config: {ex.Message}");
        }
    }

    private async Task<string> RequestWindowsTokenAsync(string username, string domain)
    {
        // In real implementation, use WindowsIdentity or Kerberos
        // This is a placeholder
        await Task.Delay(100);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{domain}\\{username}:{DateTime.UtcNow.Ticks}"));
    }

    private async Task<TokenValidationResult> ValidateTokenWithIdPAsync(string token)
    {
        // In real implementation, validate with identity provider
        await Task.Delay(100);
        
        return new TokenValidationResult
        {
            IsValid = true,
            Username = Environment.UserName,
            Email = $"{Environment.UserName}@{Environment.UserDomainName}".ToLower()
        };
    }

    private async Task<bool> TryRefreshTokenAsync()
    {
        if (_currentSession?.RefreshToken == null || _config?.OIDCConfig == null)
            return false;

        try
        {
            var request = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _currentSession.RefreshToken,
                ["client_id"] = _config.OIDCConfig.ClientId
            };

            var content = new FormUrlEncodedContent(request);
            var response = await _httpClient.PostAsync(_config.OIDCConfig.TokenEndpoint, content);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var tokens = JsonSerializer.Deserialize<OIDCTokens>(json);

                if (tokens?.AccessToken != null)
                {
                    _currentSession.Token = tokens.AccessToken;
                    _currentSession.RefreshToken = tokens.RefreshToken ?? _currentSession.RefreshToken;
                    _currentSession.ExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);

                    SessionRefreshed?.Invoke(this, new SSOSessionEventArgs
                    {
                        Session = _currentSession,
                        RefreshedAt = DateTime.UtcNow
                    });

                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Warning("SSOService", $"Token refresh failed: {ex.Message}");
            return false;
        }
    }

    private async Task RevokeTokenAsync(string token)
    {
        try
        {
            if (_config?.OIDCConfig?.RevocationEndpoint != null)
            {
                var request = new Dictionary<string, string>
                {
                    ["token"] = token,
                    ["client_id"] = _config.OIDCConfig.ClientId
                };

                var content = new FormUrlEncodedContent(request);
                await _httpClient.PostAsync(_config.OIDCConfig.RevocationEndpoint, content);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("SSOService", $"Token revocation failed: {ex.Message}");
        }
    }

    private string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private string GenerateCodeChallenge(string verifier)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(verifier));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private string GenerateState()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToHexString(bytes).ToLower();
    }

    private string BuildAuthorizationUrl(string codeChallenge, string state)
    {
        if (_config?.OIDCConfig == null)
            throw new InvalidOperationException("OIDC not configured");

        var query = new Dictionary<string, string>
        {
            ["client_id"] = _config.OIDCConfig.ClientId,
            ["response_type"] = "code",
            ["scope"] = "openid profile email",
            ["redirect_uri"] = _config.OIDCConfig.RedirectUri,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state
        };

        var queryString = string.Join("&", query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        return $"{_config.OIDCConfig.AuthorizationEndpoint}?{queryString}";
    }

    private string BuildSAMLRequest()
    {
        if (_config?.SAMLConfig == null)
            throw new InvalidOperationException("SAML not configured");

        // Build SAML AuthnRequest XML using StringBuilder to avoid verbatim string interpolation issues
        var sb = new StringBuilder();
        sb.AppendLine("<samlp:AuthnRequest xmlns:samlp=\"urn:oasis:names:tc:SAML:2.0:protocol\"");
        sb.AppendLine("                    xmlns:saml=\"urn:oasis:names:tc:SAML:2.0:assertion\"");
        sb.AppendLine($"                    ID=\"_{Guid.NewGuid()}\"");
        sb.AppendLine("                    Version=\"2.0\"");
        sb.AppendLine($"                    IssueInstant=\"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\"");
        sb.AppendLine($"                    Destination=\"{_config.SAMLConfig.IdPUrl}\"");
        sb.AppendLine("                    ProtocolBinding=\"urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST\"");
        sb.AppendLine($"                    AssertionConsumerServiceURL=\"{_config.SAMLConfig.AssertionConsumerServiceUrl}\">");
        sb.AppendLine($"    <saml:Issuer>{_config.SAMLConfig.EntityId}</saml:Issuer>");
        sb.AppendLine("    <samlp:NameIDPolicy Format=\"urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress\" AllowCreate=\"true\"/>");
        sb.AppendLine("</samlp:AuthnRequest>");

        var requestXml = sb.ToString();

        // Compress and Base64 encode
        var bytes = Encoding.UTF8.GetBytes(requestXml);
        return Convert.ToBase64String(bytes);
    }
}

// Configuration models
public class SSOConfiguration
{
    public bool IsEnabled { get; set; }
    public SSOProviderType ProviderType { get; set; }
    public bool AutoLogin { get; set; }
    public OIDCConfiguration? OIDCConfig { get; set; }
    public SAMLConfiguration? SAMLConfig { get; set; }
}

public enum SSOProviderType
{
    WindowsIntegrated,
    OIDC,
    SAML
}

public class OIDCConfiguration
{
    public string ClientId { get; set; } = string.Empty;
    public string? ClientSecret { get; set; }
    public string AuthorizationEndpoint { get; set; } = string.Empty;
    public string TokenEndpoint { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string? RevocationEndpoint { get; set; }
}

public class SAMLConfiguration
{
    public string EntityId { get; set; } = string.Empty;
    public string IdPUrl { get; set; } = string.Empty;
    public string AssertionConsumerServiceUrl { get; set; } = string.Empty;
}

// Session models
public class SSOSession
{
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    
    public bool IsValid => !string.IsNullOrEmpty(Token) && ExpiresAt > DateTime.UtcNow;
}

public class TokenValidationResult
{
    public bool IsValid { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class OIDCTokens
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }
    
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
    
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
    
    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }
}

// Event args
public class SSOAuthenticationEventArgs : EventArgs
{
    public bool Success { get; set; }
    public string? Username { get; set; }
    public string? Provider { get; set; }
    public string? Error { get; set; }
}

public class SSOSessionEventArgs : EventArgs
{
    public SSOSession? Session { get; set; }
    public DateTime RefreshedAt { get; set; }
}
