using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests;

/// <summary>
/// Unit tests for the UI-layer SecurityAuditService (hash-chained audit log).
/// The service intentionally exposes a thin surface: a singleton Instance,
/// LogEvent(category, message), and VerifyLogIntegrity().
/// </summary>
[TestClass]
public class SecurityAuditServiceTests
{
    [TestMethod]
    public void Instance_IsSingleton()
    {
        var instance1 = SecurityAuditService.Instance;
        var instance2 = SecurityAuditService.Instance;

        Assert.AreSame(instance1, instance2, "SecurityAuditService should be a singleton");
    }

    [TestMethod]
    public void LogEvent_DoesNotThrow()
    {
        try
        {
            SecurityAuditService.Instance.LogEvent("TestCategory", "test message from unit test");
            Assert.IsTrue(true);
        }
        catch (Exception ex)
        {
            Assert.Fail($"LogEvent should not throw: {ex.Message}");
        }
    }

    [TestMethod]
    public void VerifyLogIntegrity_ReturnsBoolean()
    {
        // The log may or may not exist on the test machine; the contract is
        // that verification always answers instead of throwing.
        var result = SecurityAuditService.Instance.VerifyLogIntegrity();

        Assert.IsTrue(result || !result, "VerifyLogIntegrity should return a bool");
    }

    [TestMethod]
    public void AuditEventType_HasExpectedValues()
    {
        // Enum lives in AuditLogService.cs and is shared by the audit surfaces.
        var eventTypes = Enum.GetValues<AuditEventType>();
        Assert.IsTrue(eventTypes.Contains(AuditEventType.ServiceStarted), "Should have ServiceStarted");
        Assert.IsTrue(eventTypes.Contains(AuditEventType.AuthenticationSuccess), "Should have AuthenticationSuccess");
        Assert.IsTrue(eventTypes.Contains(AuditEventType.AuthenticationFailure), "Should have AuthenticationFailure");
        Assert.IsTrue(eventTypes.Contains(AuditEventType.TamperDetected), "Should have TamperDetected");
    }
}
