using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests;

/// <summary>
/// Tests for SSOService OIDC authentication functionality.
/// </summary>
[TestClass]
public class SSOServiceOIDCTests
{
    /// <summary>
    /// Tests that PKCE code verifier is generated correctly.
    /// </summary>
    [TestMethod]
    public void GenerateCodeVerifier_ReturnsValidFormat()
    {
        // Using reflection to test private method
        var service = SSOService.Instance;
        var method = typeof(SSOService).GetMethod("GenerateCodeVerifier", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var verifier = method?.Invoke(service, null) as string;
        
        Assert.IsNotNull(verifier);
        Assert.IsTrue(verifier.Length >= 43, "Code verifier should be at least 43 characters");
        Assert.IsFalse(verifier.Contains("+"), "Verifier should not contain +");
        Assert.IsFalse(verifier.Contains("/"), "Verifier should not contain /");
        Assert.IsFalse(verifier.Contains("="), "Verifier should not contain padding =");
    }

    /// <summary>
    /// Tests that code challenge is generated correctly from verifier.
    /// </summary>
    [TestMethod]
    public void GenerateCodeChallenge_ReturnsValidS256Hash()
    {
        var service = SSOService.Instance;
        var method = typeof(SSOService).GetMethod("GenerateCodeChallenge", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var verifier = "test_verifier_123456789012345678901234567890";
        var challenge = method?.Invoke(service, new object[] { verifier }) as string;
        
        Assert.IsNotNull(challenge);
        Assert.IsFalse(challenge.Contains("+"), "Challenge should not contain +");
        Assert.IsFalse(challenge.Contains("/"), "Challenge should not contain /");
        Assert.IsFalse(challenge.Contains("="), "Challenge should not contain padding =");
        
        // Verify it's a valid base64url string
        var base64url = challenge.Replace('-', '+').Replace('_', '/');
        var padding = 4 - (base64url.Length % 4);
        if (padding != 4) base64url += new string('=', padding);
        
        var bytes = Convert.FromBase64String(base64url);
        Assert.AreEqual(32, bytes.Length, "SHA-256 hash should be 32 bytes");
    }

    /// <summary>
    /// Tests that state parameter is generated correctly.
    /// </summary>
    [TestMethod]
    public void GenerateState_ReturnsValidHexString()
    {
        var service = SSOService.Instance;
        var method = typeof(SSOService).GetMethod("GenerateState", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var state = method?.Invoke(service, null) as string;
        
        Assert.IsNotNull(state);
        Assert.AreEqual(32, state.Length, "State should be 32 hex characters (16 bytes)");
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(state, "^[a-f0-9]+$"), "State should be lowercase hex");
    }

    /// <summary>
    /// Tests SSO configuration model properties.
    /// </summary>
    [TestMethod]
    public void OIDCConfiguration_Properties_Work()
    {
        var config = new OIDCConfiguration
        {
            ClientId = "test-client-id",
            ClientSecret = "test-secret",
            AuthorizationEndpoint = "https://auth.example.com/authorize",
            TokenEndpoint = "https://auth.example.com/token",
            RedirectUri = "http://localhost:5000/callback",
            RevocationEndpoint = "https://auth.example.com/revoke"
        };

        Assert.AreEqual("test-client-id", config.ClientId);
        Assert.AreEqual("test-secret", config.ClientSecret);
        Assert.AreEqual("https://auth.example.com/authorize", config.AuthorizationEndpoint);
        Assert.AreEqual("https://auth.example.com/token", config.TokenEndpoint);
        Assert.AreEqual("http://localhost:5000/callback", config.RedirectUri);
        Assert.AreEqual("https://auth.example.com/revoke", config.RevocationEndpoint);
    }

    /// <summary>
    /// Tests SSO session model validation.
    /// </summary>
    [TestMethod]
    public void SSOSession_IsValid_Works()
    {
        var validSession = new SSOSession
        {
            Token = "valid_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        Assert.IsTrue(validSession.IsValid);

        var expiredSession = new SSOSession
        {
            Token = "valid_token",
            ExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        Assert.IsFalse(expiredSession.IsValid);

        var noTokenSession = new SSOSession
        {
            Token = "",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        Assert.IsFalse(noTokenSession.IsValid);
    }

    /// <summary>
    /// Tests OIDC tokens deserialization.
    /// </summary>
    [TestMethod]
    public void OIDCTokens_Deserialization_Works()
    {
        var json = @"{
            ""access_token"": ""test_access_token"",
            ""refresh_token"": ""test_refresh_token"",
            ""expires_in"": 3600,
            ""token_type"": ""Bearer""
        }";

        var tokens = JsonSerializer.Deserialize<OIDCTokens>(json);

        Assert.IsNotNull(tokens);
        Assert.AreEqual("test_access_token", tokens.AccessToken);
        Assert.AreEqual("test_refresh_token", tokens.RefreshToken);
        Assert.AreEqual(3600, tokens.ExpiresIn);
        Assert.AreEqual("Bearer", tokens.TokenType);
    }

    /// <summary>
    /// Tests SSO authentication event args.
    /// </summary>
    [TestMethod]
    public void SSOAuthenticationEventArgs_Properties_Work()
    {
        var args = new SSOAuthenticationEventArgs
        {
            Success = true,
            Username = "testuser",
            Provider = "OIDC",
            Error = null
        };

        Assert.IsTrue(args.Success);
        Assert.AreEqual("testuser", args.Username);
        Assert.AreEqual("OIDC", args.Provider);
        Assert.IsNull(args.Error);
    }

    /// <summary>
    /// Tests token validation result properties.
    /// </summary>
    [TestMethod]
    public void TokenValidationResult_Properties_Work()
    {
        var result = new TokenValidationResult
        {
            IsValid = true,
            Username = "testuser",
            Email = "test@example.com",
            RefreshToken = "refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("testuser", result.Username);
        Assert.AreEqual("test@example.com", result.Email);
        Assert.AreEqual("refresh_token", result.RefreshToken);
        Assert.IsNotNull(result.ExpiresAt);
    }

    /// <summary>
    /// Tests that service singleton returns same instance.
    /// </summary>
    [TestMethod]
    public void SSOService_Singleton_ReturnsSameInstance()
    {
        var instance1 = SSOService.Instance;
        var instance2 = SSOService.Instance;

        Assert.AreSame(instance1, instance2);
    }
}
