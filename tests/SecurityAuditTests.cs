using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.Core.Security;
using System.IO;
using System;

namespace Redball.Tests;

[TestClass]
public class SecurityAuditTests : IDisposable
{
    private readonly string _testLogPath;

    public SecurityAuditTests()
    {
        _testLogPath = Path.Combine(Path.GetTempPath(), $"audit_{Guid.NewGuid()}.log");
    }

    public void Dispose()
    {
        if (File.Exists(_testLogPath)) File.Delete(_testLogPath);
    }

    [TestMethod]
    public void AuditService_LogEvents_MaintainsIntegrity()
    {
        var service = new SecurityAuditService(_testLogPath);
        
        service.LogEvent("Auth", "LOGIN", "User admin logged in");
        service.LogEvent("Config", "UPDATE", "Heartbeat changed to 30s");
        
        var valid = service.VerifyIntegrity(out int failedLine, out string? error);
        
        Assert.IsTrue(valid, $"Log should be valid: {error}");
        Assert.AreEqual(0, failedLine);
    }

    [TestMethod]
    public void AuditService_TamperDetected_WhenLineModified()
    {
        var service = new SecurityAuditService(_testLogPath);
        service.LogEvent("Auth", "LOGIN", "User admin logged in");
        service.LogEvent("Auth", "LOGOUT", "User admin logged out");

        // Tamper with the file
        var lines = File.ReadAllLines(_testLogPath);
        lines[0] = lines[0].Replace("admin", "hacker");
        File.WriteAllLines(_testLogPath, lines);

        var valid = service.VerifyIntegrity(out int failedLine, out string? error);

        Assert.IsFalse(valid);
        Assert.AreEqual(1, failedLine);
        StringAssert.Contains(error?.ToLower() ?? "", "tamper detected");
    }

    [TestMethod]
    public void AuditService_DeletionDetected_InMiddleOfChain()
    {
        var service = new SecurityAuditService(_testLogPath);
        service.LogEvent("Ev1", "T", "D1");
        service.LogEvent("Ev2", "T", "D2");
        service.LogEvent("Ev3", "T", "D3");

        // Delete the second line
        var lines = File.ReadAllLines(_testLogPath);
        var newLines = new[] { lines[0], lines[2] };
        File.WriteAllLines(_testLogPath, newLines);

        var valid = service.VerifyIntegrity(out int failedLine, out string? error);

        Assert.IsFalse(valid);
        Assert.AreEqual(2, failedLine); // Line 2 (old Line 3) now has wrong PreviousHash
        StringAssert.Contains(error?.ToLower() ?? "", "broken");
    }
}
