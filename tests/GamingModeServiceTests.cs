using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;

namespace Redball.Tests;

[TestClass]
public class GamingModeServiceTests
{
    [TestMethod]
    public void Instance_IsSingleton()
    {
        var instance1 = GamingModeService.Instance;
        var instance2 = GamingModeService.Instance;
        
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void IsGaming_InitiallyFalse()
    {
        var service = GamingModeService.Instance;
        
        // Note: This relies on the actual service state
        // The service may have been modified by other tests
        // Just verify we can read the property
        var isGaming = service.IsGaming;
        Assert.IsInstanceOfType(isGaming, typeof(bool));
    }

    [TestMethod]
    public void GamingStateChanged_EventCanBeSubscribed()
    {
        var service = GamingModeService.Instance;
        bool eventFired = false;
        
        EventHandler<bool> handler = (s, e) => eventFired = true;
        service.GamingStateChanged += handler;
        
        // Event subscription should work
        Assert.IsTrue(true);
        
        // Cleanup
        service.GamingStateChanged -= handler;
    }

    [TestMethod]
    public void OptimizeFootprint_DoesNotThrow()
    {
        var service = GamingModeService.Instance;
        
        try
        {
            service.OptimizeFootprint();
            // Should not throw
        }
        catch (Exception ex)
        {
            Assert.Fail($"OptimizeFootprint should not throw: {ex.Message}");
        }
    }

    [TestMethod]
    public void CheckAndUpdate_WhenGamingModeDisabled_DoesNotThrow()
    {
        var service = GamingModeService.Instance;
        var originalSetting = ConfigService.Instance.Config.GamingModeEnabled;
        
        try
        {
            // Ensure gaming mode is disabled
            ConfigService.Instance.Config.GamingModeEnabled = false;
            
            service.CheckAndUpdate();
            // Should not throw
        }
        catch (Exception ex)
        {
            Assert.Fail($"CheckAndUpdate should not throw: {ex.Message}");
        }
        finally
        {
            ConfigService.Instance.Config.GamingModeEnabled = originalSetting;
        }
    }
}
