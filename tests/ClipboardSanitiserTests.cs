using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class ClipboardSanitiserTests
    {
        [TestMethod]
        public void Analyse_EmptyText_ReturnsNoWarnings()
        {
            var warnings = ClipboardSanitiser.Analyse("");
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void Analyse_NullText_ReturnsNoWarnings()
        {
            var warnings = ClipboardSanitiser.Analyse(null!);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void Analyse_ShortSafeText_ReturnsNoWarnings()
        {
            var warnings = ClipboardSanitiser.Analyse("Hello, world!");
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void Analyse_ExceedsMaxLength_ReturnsLengthWarning()
        {
            var text = new string('x', 6000);
            var warnings = ClipboardSanitiser.Analyse(text);
            Assert.IsTrue(warnings.Count > 0);
            Assert.IsTrue(warnings.Exists(w => w.Contains("6,000 characters")));
        }

        [TestMethod]
        public void Analyse_CustomMaxLength_RespectsThreshold()
        {
            var text = new string('x', 100);
            var warnings = ClipboardSanitiser.Analyse(text, maxSafeLength: 50);
            Assert.IsTrue(warnings.Count > 0);
            Assert.IsTrue(warnings.Exists(w => w.Contains("characters")));
        }

        [TestMethod]
        public void Analyse_CreditCardPattern_DetectsWarning()
        {
            var warnings = ClipboardSanitiser.Analyse("My card is 4111 1111 1111 1111 thanks");
            Assert.IsTrue(warnings.Exists(w => w.Contains("Credit card")));
        }

        [TestMethod]
        public void Analyse_SSNPattern_DetectsWarning()
        {
            var warnings = ClipboardSanitiser.Analyse("SSN: 123-45-6789");
            Assert.IsTrue(warnings.Exists(w => w.Contains("Social Security")));
        }

        [TestMethod]
        public void Analyse_ApiKeyPattern_DetectsWarning()
        {
            var warnings = ClipboardSanitiser.Analyse("api_key=sk_live_abcdef1234567890");
            Assert.IsTrue(warnings.Exists(w => w.Contains("API key")));
        }

        [TestMethod]
        public void Analyse_PrivateKeyHeader_DetectsWarning()
        {
            var warnings = ClipboardSanitiser.Analyse("-----BEGIN PRIVATE KEY-----\nMIIEvQ...");
            Assert.IsTrue(warnings.Exists(w => w.Contains("Private key")));
        }

        [TestMethod]
        public void Analyse_BearerToken_DetectsWarning()
        {
            var warnings = ClipboardSanitiser.Analyse("Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9");
            Assert.IsTrue(warnings.Exists(w => w.Contains("Bearer token")));
        }

        [TestMethod]
        public void Analyse_AWSAccessKey_DetectsWarning()
        {
            var warnings = ClipboardSanitiser.Analyse("aws_access_key_id = AKIAIOSFODNN7EXAMPLE");
            Assert.IsTrue(warnings.Exists(w => w.Contains("AWS access key")));
        }

        [TestMethod]
        public void Analyse_ConnectionString_DetectsWarning()
        {
            var warnings = ClipboardSanitiser.Analyse("Server=myserver.database.windows.net;Database=mydb");
            Assert.IsTrue(warnings.Exists(w => w.Contains("Connection string")));
        }

        [TestMethod]
        public void Analyse_MultiplePatterns_ReturnsMultipleWarnings()
        {
            var text = "SSN: 123-45-6789\napi_key=sk_live_abcdef1234567890";
            var warnings = ClipboardSanitiser.Analyse(text);
            Assert.IsTrue(warnings.Count >= 2);
        }

        [TestMethod]
        public void IsSafe_SafeText_ReturnsTrue()
        {
            Assert.IsTrue(ClipboardSanitiser.IsSafe("Just a normal message"));
        }

        [TestMethod]
        public void IsSafe_SensitiveText_ReturnsFalse()
        {
            Assert.IsFalse(ClipboardSanitiser.IsSafe("SSN: 123-45-6789"));
        }

        [TestMethod]
        public void Analyse_RSAPrivateKey_DetectsWarning()
        {
            var warnings = ClipboardSanitiser.Analyse("-----BEGIN RSA PRIVATE KEY-----\nMIIBog...");
            Assert.IsTrue(warnings.Exists(w => w.Contains("Private key")));
        }
    }
}
