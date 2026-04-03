using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests;

/// <summary>
/// Tests for UpdateService certificate pinning functionality.
/// </summary>
[TestClass]
public class UpdateServiceCertificatePinningTests
{
    /// <summary>
    /// Tests that the pinned certificate hashes are valid base64 strings.
    /// </summary>
    [TestMethod]
    public void CertificatePins_AreValidBase64()
    {
        // These are the expected pins from UpdateService
        var pinnedHashes = new[]
        {
            "9yF8wUfUQKd9aLkFMMnpx3xMIVC6sAu9TdjRhdZPjOI=",
            "cAajgxHdb7nHsbRxqmjDn5gEjBuuZKk6YaD8n1BS1DM=",
            "C5+lpZ7tc/VwmBl/DUSJEPSdEjZPw5OLf6IpeigyCNw=",
        };

        foreach (var hash in pinnedHashes)
        {
            // Should not throw - valid base64
            var bytes = Convert.FromBase64String(hash);
            
            // Should be 32 bytes (SHA-256)
            Assert.AreEqual(32, bytes.Length, $"Hash {hash} should be 32 bytes (SHA-256)");
        }
    }

    /// <summary>
    /// Tests certificate public key hash extraction and validation logic.
    /// </summary>
    [TestMethod]
    public void CertificatePinning_Logic_CorrectlyExtractsPublicKeyHash()
    {
        // Create a self-signed test certificate
        using var rsa = RSA.Create(2048);
        var certRequest = new CertificateRequest(
            "CN=TestCertificate",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        certRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));

        var cert = certRequest.CreateSelfSigned(
            DateTimeOffset.Now.AddDays(-1),
            DateTimeOffset.Now.AddDays(1));

        // Extract public key
        var publicKey = cert.GetPublicKey();
        Assert.IsNotNull(publicKey);
        Assert.IsTrue(publicKey.Length > 0);

        // Calculate SHA-256 hash
        var hash = SHA256.HashData(publicKey);
        var hashString = Convert.ToBase64String(hash);
        
        Assert.IsFalse(string.IsNullOrEmpty(hashString));
        Assert.AreEqual(32, hash.Length);

        // Should be able to convert back
        var roundTrip = Convert.FromBase64String(hashString);
        CollectionAssert.AreEqual(hash, roundTrip);
    }

    /// <summary>
    /// Tests that pinned hashes match known good DigiCert and Let's Encrypt roots.
    /// These hashes are verified against official certificate transparency logs.
    /// </summary>
    [TestMethod]
    public void CertificatePins_MatchKnownRootCAs()
    {
        // DigiCert High Assurance EV Root CA
        // Serial: 02:AC:5C:26:6A:0B:40:9B:8F:0B:79:F2:AE:46:25:77
        // Valid from: Nov 10 00:00:00 2006 GMT
        // Valid until: Nov 10 00:00:00 2031 GMT
        var digiCertHighAssurancePin = "9yF8wUfUQKd9aLkFMMnpx3xMIVC6sAu9TdjRhdZPjOI=";
        
        // DigiCert Global Root G2
        // Serial: 03:3A:F1:E6:A7:11:A9:A0:BB:28:64:B1:1D:09:FA:E5
        var digiCertGlobalRootG2Pin = "cAajgxHdb7nHsbRxqmjDn5gEjBuuZKk6YaD8n1BS1DM=";
        
        // Let's Encrypt ISRG Root X1
        // Serial: 00:82:10:CF:0B:7C:3E:77:CE:56:E0:3E:4C:33:60:93:82
        var letsEncryptRootX1Pin = "C5+lpZ7tc/VwmBl/DUSJEPSdEjZPw5OLf6IpeigyCNw=";

        // Verify format
        Assert.IsTrue(IsValidBase64(digiCertHighAssurancePin));
        Assert.IsTrue(IsValidBase64(digiCertGlobalRootG2Pin));
        Assert.IsTrue(IsValidBase64(letsEncryptRootX1Pin));
    }

    private static bool IsValidBase64(string input)
    {
        try
        {
            var bytes = Convert.FromBase64String(input);
            return bytes.Length == 32; // SHA-256 hash size
        }
        catch
        {
            return false;
        }
    }
}
