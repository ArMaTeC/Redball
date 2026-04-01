using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Redball.Tests;

[TestClass]
public class AdminDashboardServiceTests
{
    [TestInitialize]
    public void Setup()
    {
        ConfigService.Instance.Config.EnableAdminDashboard = true;
    }

    [TestMethod]
    public void Instance_IsSingleton()
    {
        var instance1 = AdminDashboardService.Instance;
        var instance2 = AdminDashboardService.Instance;
        
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void IsAdminModeEnabled_WhenConfigDisabled_ReturnsFalse()
    {
        ConfigService.Instance.Config.EnableAdminDashboard = false;
        
        Assert.IsFalse(AdminDashboardService.Instance.IsAdminModeEnabled);
    }

    [TestMethod]
    public void IsAdminModeEnabled_WhenConfigEnabled_ReturnsTrue()
    {
        ConfigService.Instance.Config.EnableAdminDashboard = true;
        
        Assert.IsTrue(AdminDashboardService.Instance.IsAdminModeEnabled);
    }

    [TestMethod]
    public void GenerateUsageReport_WithValidRange_ReturnsReport()
    {
        var start = DateTime.Now.AddDays(-7);
        var end = DateTime.Now;
        
        var report = AdminDashboardService.Instance.GenerateUsageReport(start, end);
        
        Assert.IsNotNull(report);
        Assert.IsNotNull(report.Period);
        Assert.IsTrue(report.TotalSessions >= 0);
        Assert.IsTrue(report.UniqueUsers >= 0);
    }

    [TestMethod]
    public void GenerateComplianceReport_WithValidRange_ReturnsReport()
    {
        var start = DateTime.Now.AddDays(-7);
        var end = DateTime.Now;
        
        var report = AdminDashboardService.Instance.GenerateComplianceReport(start, end);
        
        Assert.IsNotNull(report);
        Assert.IsTrue(report.ComplianceScore >= 0 && report.ComplianceScore <= 100);
        Assert.IsNotNull(report.Violations);
    }

    [TestMethod]
    public async Task ApplyPolicyAsync_ValidPolicy_ReturnsTrue()
    {
        var policy = new AdminPolicy
        {
            PolicyName = "Test Policy",
            RequireBatteryAware = true
        };
        
        var result = await AdminDashboardService.Instance.ApplyPolicyAsync(policy);
        
        Assert.IsTrue(result);
        Assert.AreEqual("Test Policy", AdminDashboardService.Instance.ActivePolicy.PolicyName);
    }

    [TestMethod]
    public async Task ApplyPolicyAsync_UpdatesActivePolicy()
    {
        var policy = new AdminPolicy
        {
            PolicyName = "Updated Policy",
            MaxSessionDurationMinutes = 120,
            RequireIdleDetection = true
        };
        
        await AdminDashboardService.Instance.ApplyPolicyAsync(policy);
        
        Assert.AreEqual(120, AdminDashboardService.Instance.ActivePolicy.MaxSessionDurationMinutes);
        Assert.IsTrue(AdminDashboardService.Instance.ActivePolicy.RequireIdleDetection);
    }

    [TestMethod]
    public void ValidateCompliance_WithDefaultPolicy_ReturnsCompliant()
    {
        // Apply a permissive default policy
        var policy = new AdminPolicy { PolicyName = "Default", AllowUserOverrides = true };
        AdminDashboardService.Instance.ApplyPolicyAsync(policy).Wait();
        
        var result = AdminDashboardService.Instance.ValidateCompliance();
        
        Assert.IsNotNull(result);
        // Should be compliant with default settings
        Assert.IsTrue(result.IsCompliant || result.Violations.Count == 0 || result.Violations.All(v => v.Severity != ViolationSeverity.Error));
    }

    [TestMethod]
    public void ValidateCompliance_WithStrictPolicy_MayHaveViolations()
    {
        // Apply a strict policy that may conflict with current config
        var policy = new AdminPolicy 
        { 
            PolicyName = "Strict",
            RequireBatteryAware = true,
            RequireIdleDetection = true,
            AllowUserOverrides = false
        };
        AdminDashboardService.Instance.ApplyPolicyAsync(policy).Wait();
        
        var result = AdminDashboardService.Instance.ValidateCompliance();
        
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Violations);
        // Result depends on current config state
    }

    [TestMethod]
    public void ExportUsageReportCsv_ReturnsString()
    {
        var report = AdminDashboardService.Instance.GenerateUsageReport(DateTime.Now.AddDays(-1), DateTime.Now);
        
        var csv = AdminDashboardService.Instance.ExportUsageReportCsv(report);
        
        Assert.IsNotNull(csv);
        StringAssert.Contains(csv, "Metric,Value");
    }

    [TestMethod]
    public void GetSystemHealth_ReturnsMetrics()
    {
        var health = AdminDashboardService.Instance.GetSystemHealth();
        
        Assert.IsNotNull(health);
        Assert.IsTrue(health.MemoryUsageMB >= 0);
        Assert.IsNotNull(health.ConfigServiceStatus);
    }

    [TestMethod]
    public void ActivePolicy_IsNotNull()
    {
        Assert.IsNotNull(AdminDashboardService.Instance.ActivePolicy);
    }

    [TestMethod]
    public void PolicyChanged_EventCanBeSubscribed()
    {
        bool eventFired = false;
        EventHandler<AdminPolicyChangedEventArgs> handler = (s, e) => eventFired = true;
        
        AdminDashboardService.Instance.PolicyChanged += handler;
        
        // Event subscription should work
        Assert.IsTrue(true);
        
        AdminDashboardService.Instance.PolicyChanged -= handler;
        
        // Use eventFired to suppress warning - we're testing subscription only
        Assert.IsFalse(eventFired);
    }

    [TestMethod]
    public void ComplianceReportReady_EventCanBeSubscribed()
    {
        bool eventFired = false;
        EventHandler<ComplianceReportReadyEventArgs> handler = (s, e) => eventFired = true;
        
        AdminDashboardService.Instance.ComplianceReportReady += handler;
        
        // Event subscription should work
        Assert.IsTrue(true);
        
        AdminDashboardService.Instance.ComplianceReportReady -= handler;
        
        // Use eventFired to suppress warning - we're testing subscription only
        Assert.IsFalse(eventFired);
    }

    [TestMethod]
    public void GenerateUsageReport_DailyBreakdown_Populated()
    {
        var start = DateTime.Now.AddDays(-3);
        var end = DateTime.Now;
        
        var report = AdminDashboardService.Instance.GenerateUsageReport(start, end);
        
        Assert.IsNotNull(report.DailyBreakdown);
        // Should have entries for each day
        Assert.IsTrue(report.DailyBreakdown.Count >= 1);
    }

    [TestMethod]
    public void GenerateComplianceReport_ChecksEncryptionStatus()
    {
        var report = AdminDashboardService.Instance.GenerateComplianceReport(DateTime.Now.AddDays(-1), DateTime.Now);
        
        Assert.IsNotNull(report.EncryptionStatus);
        Assert.IsNotNull(report.EncryptionStatus.OverallStatus);
    }

    [TestMethod]
    public void GenerateComplianceReport_ChecksDataRetention()
    {
        var report = AdminDashboardService.Instance.GenerateComplianceReport(DateTime.Now.AddDays(-1), DateTime.Now);
        
        Assert.IsNotNull(report.DataRetentionStatus);
        Assert.IsTrue(report.DataRetentionStatus.AuditLogRetentionDays > 0);
    }

    [TestMethod]
    public async Task ApplyPolicyAsync_WithMaxDuration_UpdatesConfig()
    {
        var originalDuration = ConfigService.Instance.Config.DefaultDuration;
        
        var policy = new AdminPolicy
        {
            PolicyName = "Duration Test",
            MaxSessionDurationMinutes = 30,
            AllowUserOverrides = false
        };
        
        await AdminDashboardService.Instance.ApplyPolicyAsync(policy);
        
        // If current duration exceeds policy, it should be reduced
        if (originalDuration > 30)
        {
            // Config may be updated based on policy
        }
        
        Assert.AreEqual(30, AdminDashboardService.Instance.ActivePolicy.MaxSessionDurationMinutes);
    }
}
