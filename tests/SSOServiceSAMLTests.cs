using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests;

/// <summary>
/// Tests for SSOService SAML authentication functionality.
/// </summary>
[TestClass]
public class SSOServiceSAMLTests
{
    /// <summary>
    /// Tests SAML configuration model properties.
    /// </summary>
    [TestMethod]
    public void SAMLConfiguration_Properties_Work()
    {
        var config = new SAMLConfiguration
        {
            EntityId = "https://redball.example.com/sp",
            IdPUrl = "https://idp.example.com/saml",
            AssertionConsumerServiceUrl = "http://localhost:5000/saml/acs"
        };

        Assert.AreEqual("https://redball.example.com/sp", config.EntityId);
        Assert.AreEqual("https://idp.example.com/saml", config.IdPUrl);
        Assert.AreEqual("http://localhost:5000/saml/acs", config.AssertionConsumerServiceUrl);
    }

    /// <summary>
    /// Tests SAML assertion model properties.
    /// </summary>
    [TestMethod]
    public void SAMLAssertion_Properties_Work()
    {
        var assertion = new SAMLAssertion
        {
            AssertionID = "_1234567890abcdef",
            NameID = "user@example.com",
            NotOnOrAfter = DateTime.UtcNow.AddHours(1),
            Attributes = new Dictionary<string, string>
            {
                ["email"] = "user@example.com",
                ["role"] = "admin"
            }
        };

        Assert.AreEqual("_1234567890abcdef", assertion.AssertionID);
        Assert.AreEqual("user@example.com", assertion.NameID);
        Assert.IsTrue(assertion.NotOnOrAfter > DateTime.UtcNow);
        Assert.AreEqual(2, assertion.Attributes.Count);
        Assert.AreEqual("admin", assertion.Attributes["role"]);
    }

    /// <summary>
    /// Tests SAML response parsing with valid XML.
    /// </summary>
    [TestMethod]
    public void ParseSAMLResponseAsync_ValidXml_ReturnsAssertion()
    {
        var now = DateTime.UtcNow;
        var futureTime = now.AddHours(1);
        var validSamlResponse = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol""
                xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion""
                ID=""_response123""
                Version=""2.0""
                IssueInstant=""{now:yyyy-MM-ddTHH:mm:ssZ}""
                Destination=""http://localhost:5000/saml/acs"">
    <samlp:Status>
        <samlp:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success""/>
    </samlp:Status>
    <saml:Assertion ID=""_assertion123""
                    Version=""2.0""
                    IssueInstant=""{now:yyyy-MM-ddTHH:mm:ssZ}"">
        <saml:Issuer>https://idp.example.com</saml:Issuer>
        <saml:Subject>
            <saml:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">
                user@example.com
            </saml:NameID>
        </saml:Subject>
        <saml:Conditions NotBefore=""{now:yyyy-MM-ddTHH:mm:ssZ}"" NotOnOrAfter=""{futureTime:yyyy-MM-ddTHH:mm:ssZ}""/>
        <saml:AttributeStatement>
            <saml:Attribute Name=""email"">
                <saml:AttributeValue>user@example.com</saml:AttributeValue>
            </saml:Attribute>
            <saml:Attribute Name=""role"">
                <saml:AttributeValue>user</saml:AttributeValue>
            </saml:Attribute>
        </saml:AttributeStatement>
    </saml:Assertion>
</samlp:Response>";

        // Use reflection to test private method
        var service = SSOService.Instance;
        var method = typeof(SSOService).GetMethod("ParseSAMLResponseAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var task = method?.Invoke(service, new object[] { validSamlResponse }) as System.Threading.Tasks.Task<SAMLAssertion>;
        var result = task?.GetAwaiter().GetResult();

        Assert.IsNotNull(result);
        Assert.AreEqual("_assertion123", result.AssertionID);
        Assert.AreEqual("user@example.com", result.NameID);
        Assert.IsTrue(result.NotOnOrAfter > DateTime.UtcNow);
        Assert.IsTrue(result.Attributes.ContainsKey("email"));
        Assert.AreEqual("user@example.com", result.Attributes["email"]);
    }

    /// <summary>
    /// Tests SAML response parsing with failed status.
    /// </summary>
    [TestMethod]
    public void ParseSAMLResponseAsync_FailedStatus_ReturnsNull()
    {
        var failedSamlResponse = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol""
                xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion""
                ID=""_response123""
                Version=""2.0"">
    <samlp:Status>
        <samlp:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:AuthnFailed""/>
    </samlp:Status>
</samlp:Response>";

        var service = SSOService.Instance;
        var method = typeof(SSOService).GetMethod("ParseSAMLResponseAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var task = method?.Invoke(service, new object[] { failedSamlResponse }) as System.Threading.Tasks.Task<SAMLAssertion>;
        var result = task?.GetAwaiter().GetResult();

        Assert.IsNull(result);
    }

    /// <summary>
    /// Tests SAML response parsing with invalid XML.
    /// </summary>
    [TestMethod]
    public void ParseSAMLResponseAsync_InvalidXml_ReturnsNull()
    {
        var invalidXml = "This is not valid XML";

        var service = SSOService.Instance;
        var method = typeof(SSOService).GetMethod("ParseSAMLResponseAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var task = method?.Invoke(service, new object[] { invalidXml }) as System.Threading.Tasks.Task<SAMLAssertion>;
        var result = task?.GetAwaiter().GetResult();

        Assert.IsNull(result);
    }

    /// <summary>
    /// Tests SSOConfiguration with SAML provider type.
    /// </summary>
    [TestMethod]
    public void SSOConfiguration_WithSAML_Works()
    {
        var config = new SSOConfiguration
        {
            IsEnabled = true,
            ProviderType = SSOProviderType.SAML,
            AutoLogin = false,
            SAMLConfig = new SAMLConfiguration
            {
                EntityId = "https://sp.example.com",
                IdPUrl = "https://idp.example.com/saml/sso",
                AssertionConsumerServiceUrl = "https://sp.example.com/saml/acs"
            }
        };

        Assert.IsTrue(config.IsEnabled);
        Assert.AreEqual(SSOProviderType.SAML, config.ProviderType);
        Assert.IsNotNull(config.SAMLConfig);
        Assert.AreEqual("https://idp.example.com/saml/sso", config.SAMLConfig.IdPUrl);
    }

    /// <summary>
    /// Tests SSO provider type enum values.
    /// </summary>
    [TestMethod]
    public void SSOProviderType_EnumValues_AreCorrect()
    {
        Assert.AreEqual(0, (int)SSOProviderType.WindowsIntegrated);
        Assert.AreEqual(1, (int)SSOProviderType.OIDC);
        Assert.AreEqual(2, (int)SSOProviderType.SAML);
    }
}
