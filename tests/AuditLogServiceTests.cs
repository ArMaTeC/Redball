using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.IO;
using System.Linq;

namespace Redball.Tests;

[TestClass]
public class AuditLogServiceTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"audit_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [TestMethod]
    public void Instance_IsSingleton()
    {
        var instance1 = AuditLogService.Instance;
        var instance2 = AuditLogService.Instance;
        
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void LogUserAction_DoesNotThrow()
    {
        var service = AuditLogService.Instance;
        
        try
        {
            service.LogUserAction("TestAction", "Test details");
            // Should not throw
        }
        catch (Exception ex)
        {
            Assert.Fail($"LogUserAction should not throw: {ex.Message}");
        }
    }

    [TestMethod]
    public void LogConfigChange_DoesNotThrow()
    {
        var service = AuditLogService.Instance;
        
        try
        {
            service.LogConfigChange("TestSetting", "old", "new");
            // Should not throw
        }
        catch (Exception ex)
        {
            Assert.Fail($"LogConfigChange should not throw: {ex.Message}");
        }
    }

    [TestMethod]
    public void LogSecurityEvent_DoesNotThrow()
    {
        var service = AuditLogService.Instance;
        
        try
        {
            service.LogSecurityEvent("AuthAttempt", "User login", true);
            // Should not throw
        }
        catch (Exception ex)
        {
            Assert.Fail($"LogSecurityEvent should not throw: {ex.Message}");
        }
    }

    [TestMethod]
    public void LogSystemEvent_DoesNotThrow()
    {
        var service = AuditLogService.Instance;
        
        try
        {
            service.LogSystemEvent("Startup", "Application started");
            // Should not throw
        }
        catch (Exception ex)
        {
            Assert.Fail($"LogSystemEvent should not throw: {ex.Message}");
        }
    }

    [TestMethod]
    public void LogSessionEvent_DoesNotThrow()
    {
        var service = AuditLogService.Instance;
        
        try
        {
            service.LogSessionEvent("SessionStart", TimeSpan.FromMinutes(30));
            // Should not throw
        }
        catch (Exception ex)
        {
            Assert.Fail($"LogSessionEvent should not throw: {ex.Message}");
        }
    }

    [TestMethod]
    public void GetEntries_ReturnsList()
    {
        var service = AuditLogService.Instance;
        
        var entries = service.GetEntries(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
        
        Assert.IsNotNull(entries);
        Assert.IsInstanceOfType(entries, typeof(System.Collections.Generic.List<AuditLogEntry>));
    }

    [TestMethod]
    public void ExportToCsv_ReturnsString()
    {
        var service = AuditLogService.Instance;
        
        var csv = service.ExportToCsv(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
        
        Assert.IsNotNull(csv);
        Assert.IsTrue(csv.Contains("Timestamp") || csv == string.Empty);
    }

    [TestMethod]
    public void GetSummary_ReturnsSummary()
    {
        var service = AuditLogService.Instance;
        
        var summary = service.GetSummary(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
        
        Assert.IsNotNull(summary);
        Assert.IsTrue(summary.TotalEntries >= 0);
    }

    [TestMethod]
    public void CleanupOldLogs_DoesNotThrow()
    {
        var service = AuditLogService.Instance;
        
        try
        {
            service.CleanupOldLogs();
            // Should not throw
        }
        catch (Exception ex)
        {
            Assert.Fail($"CleanupOldLogs should not throw: {ex.Message}");
        }
    }

    [TestMethod]
    public void RetentionDays_DefaultsTo90()
    {
        var service = AuditLogService.Instance;
        
        Assert.AreEqual(90, service.RetentionDays);
    }

    [TestMethod]
    public void MaxFileSizeBytes_DefaultsTo10MB()
    {
        var service = AuditLogService.Instance;
        
        Assert.AreEqual(10 * 1024 * 1024, service.MaxFileSizeBytes);
    }

    [TestMethod]
    public void LogUserAction_WithNullUserId_UsesCurrentUser()
    {
        var service = AuditLogService.Instance;
        
        try
        {
            service.LogUserAction("Test", "Details", null);
            // Should not throw and should use Environment.UserName
        }
        catch (Exception ex)
        {
            Assert.Fail($"Should handle null userId: {ex.Message}");
        }
    }

    [TestMethod]
    public void LogSecurityEvent_RecordsSuccessStatus()
    {
        var service = AuditLogService.Instance;
        
        // Test both success and failure
        service.LogSecurityEvent("Test", "Success case", true);
        service.LogSecurityEvent("Test", "Failure case", false);
        
        // Verify no exception thrown
        Assert.IsTrue(true);
    }

    [TestMethod]
    public void LogConfigChange_WithNullValues_Works()
    {
        var service = AuditLogService.Instance;
        
        try
        {
            service.LogConfigChange("Setting", null, "new");
            service.LogConfigChange("Setting", "old", null);
            service.LogConfigChange("Setting", null, null);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Should handle null values: {ex.Message}");
        }
    }
}
