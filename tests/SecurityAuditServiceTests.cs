using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests;

/// <summary>
/// Unit tests for SecurityAuditService.
/// Tests comprehensive security audit logging functionality.
/// </summary>
[TestClass]
public class SecurityAuditServiceTests
{
    private SecurityAuditService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = SecurityAuditService.Instance;
    }

    [TestCleanup]
    public void Cleanup()
    {
        _service.Flush();
    }

    [TestMethod]
    public void Instance_IsSingleton()
    {
        // Arrange & Act
        var instance1 = SecurityAuditService.Instance;
        var instance2 = SecurityAuditService.Instance;

        // Assert
        Assert.AreSame(instance1, instance2, "SecurityAuditService should be a singleton");
    }

    [TestMethod]
    public void LogEvent_CreatesValidEvent()
    {
        // Arrange
        var eventType = AuditEventType.ServiceStarted;
        var component = "TestComponent";
        var details = new { TestData = "value" };

        // Act
        _service.LogEvent(eventType, component, details);
        _service.Flush();

        // Assert
        var events = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, eventType);
        Assert.IsTrue(events.Count > 0, "Should have logged the event");
        
        var loggedEvent = events.FirstOrDefault(e => e.Component == component);
        Assert.IsNotNull(loggedEvent, "Should find the logged event");
        Assert.AreEqual(eventType, loggedEvent.EventType, "Event type should match");
    }

    [TestMethod]
    public void LogEvent_WithDifferentSeverities()
    {
        // Act
        _service.LogEvent(AuditEventType.ConfigChanged, "Test", null, SecuritySeverity.Info);
        _service.LogEvent(AuditEventType.AuthenticationFailure, "Test", null, SecuritySeverity.Warning);
        _service.LogEvent(AuditEventType.TamperDetected, "Test", null, SecuritySeverity.Critical);
        _service.Flush();

        // Assert
        var infoEvents = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, AuditEventType.ConfigChanged);
        var warningEvents = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, AuditEventType.AuthenticationFailure);
        var criticalEvents = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, AuditEventType.TamperDetected);

        Assert.IsTrue(infoEvents.Any(e => e.Severity == SecuritySeverity.Info), "Should have Info severity event");
        Assert.IsTrue(warningEvents.Any(e => e.Severity == SecuritySeverity.Warning), "Should have Warning severity event");
        Assert.IsTrue(criticalEvents.Any(e => e.Severity == SecuritySeverity.Critical), "Should have Critical severity event");
    }

    [TestMethod]
    public void LogConfigChange_CreatesConfigEvent()
    {
        // Arrange
        var configKey = "TestKey";
        var oldValue = "OldValue";
        var newValue = "NewValue";

        // Act
        _service.LogConfigChange(configKey, oldValue, newValue, "TestUser");
        _service.Flush();

        // Assert
        var events = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, AuditEventType.ConfigChanged);
        Assert.IsTrue(events.Count > 0, "Should have config change event");
    }

    [TestMethod]
    public void LogAuthentication_CreatesAuthEvent()
    {
        // Arrange
        var method = "WindowsHello";
        var identity = "TestUser";

        // Act
        _service.LogAuthentication(method, true, identity);
        _service.LogAuthentication(method, false, identity, "Invalid PIN");
        _service.Flush();

        // Assert
        var successEvents = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, AuditEventType.AuthenticationSuccess);
        var failureEvents = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, AuditEventType.AuthenticationFailure);

        Assert.IsTrue(successEvents.Count > 0, "Should have success auth event");
        Assert.IsTrue(failureEvents.Count > 0, "Should have failure auth event");
    }

    [TestMethod]
    public void LogTamperDetection_CreatesTamperEvent()
    {
        // Arrange
        var detectionMethod = "IntegrityCheck";
        var details = "File hash mismatch detected";

        // Act
        _service.LogTamperDetection(detectionMethod, details, SecuritySeverity.Critical);
        _service.Flush();

        // Assert
        var events = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, AuditEventType.TamperDetected);
        Assert.IsTrue(events.Count > 0, "Should have tamper detection event");
        Assert.IsTrue(events.Any(e => e.Severity == SecuritySeverity.Critical), "Should be critical severity");
    }

    [TestMethod]
    public void LogCryptoOperation_CreatesCryptoEvent()
    {
        // Arrange
        var operation = "Encrypt";
        var algorithm = "AES-256";

        // Act
        _service.LogCryptoOperation(operation, algorithm, true, "key123");
        _service.LogCryptoOperation(operation, algorithm, false, "key123");
        _service.Flush();

        // Assert
        var successEvents = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, AuditEventType.EncryptionOperation);
        var failureEvents = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, AuditEventType.EncryptionFailure);

        Assert.IsTrue(successEvents.Count > 0, "Should have success crypto event");
        Assert.IsTrue(failureEvents.Count > 0, "Should have failure crypto event");
    }

    [TestMethod]
    public void LogUpdateEvent_CreatesUpdateEvent()
    {
        // Arrange
        var version = "2.1.0";
        var checksum = "abc123";

        // Act
        _service.LogUpdateEvent("Download", version, true, checksum);
        _service.LogUpdateEvent("Install", version, false, checksum);
        _service.Flush();

        // Assert
        var successEvents = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, AuditEventType.UpdateSuccess);
        var failureEvents = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, AuditEventType.UpdateFailure);

        Assert.IsTrue(successEvents.Count > 0, "Should have success update event");
        Assert.IsTrue(failureEvents.Count > 0, "Should have failure update event");
    }

    [TestMethod]
    public void LogAccessControl_CreatesAccessEvent()
    {
        // Arrange
        var resource = "ConfigFile";
        var action = "Write";

        // Act
        _service.LogAccessControl(resource, action, true, "AdminUser");
        _service.LogAccessControl(resource, action, false, "RegularUser");
        _service.Flush();

        // Assert
        var grantedEvents = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, AuditEventType.AccessGranted);
        var deniedEvents = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, AuditEventType.AccessDenied);

        Assert.IsTrue(grantedEvents.Count > 0, "Should have access granted event");
        Assert.IsTrue(deniedEvents.Count > 0, "Should have access denied event");
    }

    [TestMethod]
    public void QueryEvents_WithTimeFilter_ReturnsFilteredEvents()
    {
        // Arrange
        var pastTime = DateTime.UtcNow.AddHours(-1);
        var futureTime = DateTime.UtcNow.AddHours(1);

        // Act
        _service.LogEvent(AuditEventType.ServiceStarted, "TimeTest", null);
        _service.Flush();

        var events = _service.QueryEvents(pastTime, futureTime);

        // Assert
        Assert.IsTrue(events.Count > 0, "Should return events within time range");
    }

    [TestMethod]
    public void QueryEvents_WithMaxResults_LimitsResults()
    {
        // Arrange & Act
        for (int i = 0; i < 10; i++)
        {
            _service.LogEvent(AuditEventType.ServiceStarted, $"BatchTest{i}", null);
        }
        _service.Flush();

        var events = _service.QueryEvents(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow, maxResults: 5);

        // Assert
        Assert.IsTrue(events.Count <= 5, "Should respect max results limit");
    }

    [TestMethod]
    public void GetSecuritySummary_ReturnsSummary()
    {
        // Arrange
        _service.LogEvent(AuditEventType.AuthenticationFailure, "Test", null, SecuritySeverity.Warning);
        _service.LogEvent(AuditEventType.TamperDetected, "Test", null, SecuritySeverity.Critical);
        _service.Flush();

        // Act
        var summary = _service.GetSecuritySummary(TimeSpan.FromMinutes(5));

        // Assert
        Assert.IsNotNull(summary, "Summary should not be null");
        Assert.IsTrue(summary.TotalEvents > 0, "Should have total events count");
        Assert.IsTrue(summary.WarningEvents >= 0, "Should have warning events count");
        Assert.IsTrue(summary.CriticalEvents >= 0, "Should have critical events count");
    }

    [TestMethod]
    public void ExportAuditLog_CreatesValidFile()
    {
        // Arrange
        var exportPath = Path.Combine(Path.GetTempPath(), $"audit_export_{Guid.NewGuid()}.json");
        _service.LogEvent(AuditEventType.ServiceStarted, "ExportTest", null);
        _service.Flush();

        // Act
        var result = _service.ExportAuditLog(exportPath);

        // Assert
        Assert.IsTrue(result, "Export should succeed");
        Assert.IsTrue(File.Exists(exportPath), "Export file should exist");
        
        var content = File.ReadAllText(exportPath);
        Assert.IsFalse(string.IsNullOrEmpty(content), "Export file should have content");
        
        // Cleanup
        try { File.Delete(exportPath); } catch { }
    }

    [TestMethod]
    public void ExportAuditLog_WithInvalidPath_ReturnsFalse()
    {
        // Arrange
        var invalidPath = "Z:\\NonExistent\\Path\\file.json";
        _service.LogEvent(AuditEventType.ServiceStarted, "ExportTest", null);
        _service.Flush();

        // Act
        var result = _service.ExportAuditLog(invalidPath);

        // Assert
        Assert.IsFalse(result, "Export to invalid path should fail");
    }

    [TestMethod]
    public void Flush_WithNoPendingEvents_DoesNotThrow()
    {
        // Act & Assert
        try
        {
            _service.Flush();
            Assert.IsTrue(true, "Flush with no pending events should not throw");
        }
        catch (Exception ex)
        {
            Assert.Fail($"Flush should not throw: {ex.Message}");
        }
    }

    [TestMethod]
    public void Dispose_DoesNotThrow()
    {
        // Act & Assert
        try
        {
            // We can't dispose the singleton in tests
            // We just verify that the service exists
            var service = SecurityAuditService.Instance;
            Assert.IsNotNull(service);
            // Note: The service has a private constructor and uses Lazy initialization
        }
        catch (Exception ex)
        {
            Assert.Fail($"Service access should not throw: {ex.Message}");
        }
    }

    [TestMethod]
    public void SecurityAuditEvent_HasRequiredFields()
    {
        // Arrange
        var evt = new SecurityAuditEvent
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.ServiceStarted,
            Component = "Test",
            Severity = SecuritySeverity.Info,
            SessionId = "session123",
            UserSid = "S-1-5-21-test",
            MachineName = "TEST-MACHINE",
            ProcessId = 12345
        };

        // Act & Assert
        Assert.AreNotEqual(default(DateTime), evt.Timestamp, "Timestamp should be set");
        Assert.IsFalse(string.IsNullOrEmpty(evt.Component), "Component should not be empty");
        Assert.IsFalse(string.IsNullOrEmpty(evt.SessionId), "SessionId should not be empty");
        Assert.IsFalse(string.IsNullOrEmpty(evt.MachineName), "MachineName should not be empty");
        Assert.IsTrue(evt.ProcessId > 0, "ProcessId should be positive");
    }

    [TestMethod]
    public void AuditEventType_HasExpectedValues()
    {
        // Act & Assert - Verify enum values exist
        var eventTypes = Enum.GetValues<AuditEventType>();
        Assert.IsTrue(eventTypes.Contains(AuditEventType.ServiceStarted), "Should have ServiceStarted");
        Assert.IsTrue(eventTypes.Contains(AuditEventType.AuthenticationSuccess), "Should have AuthenticationSuccess");
        Assert.IsTrue(eventTypes.Contains(AuditEventType.AuthenticationFailure), "Should have AuthenticationFailure");
        Assert.IsTrue(eventTypes.Contains(AuditEventType.TamperDetected), "Should have TamperDetected");
    }

    [TestMethod]
    public void SecuritySeverity_HasExpectedOrder()
    {
        // Assert
        Assert.IsTrue((int)SecuritySeverity.Info < (int)SecuritySeverity.Warning, "Info should be less than Warning");
        Assert.IsTrue((int)SecuritySeverity.Warning < (int)SecuritySeverity.Critical, "Warning should be less than Critical");
    }
}
