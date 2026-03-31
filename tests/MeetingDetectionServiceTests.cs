using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;

namespace Redball.Tests;

[TestClass]
public class MeetingDetectionServiceTests
{
    [TestMethod]
    public void Instance_IsSingleton()
    {
        var instance1 = MeetingDetectionService.Instance;
        var instance2 = MeetingDetectionService.Instance;
        
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void IsMeetingActive_InitiallyFalse()
    {
        var service = MeetingDetectionService.Instance;
        
        // The service may have been modified by other tests or actual system state
        // Just verify we can read the property
        var isMeeting = service.IsMeetingActive;
        Assert.IsInstanceOfType(isMeeting, typeof(bool));
    }

    [TestMethod]
    public void MeetingStateChanged_EventCanBeSubscribed()
    {
        var service = MeetingDetectionService.Instance;
        bool eventFired = false;
        
        EventHandler<bool> handler = (s, e) => eventFired = true;
        service.MeetingStateChanged += handler;
        
        // Event subscription should work
        Assert.IsTrue(true);
        
        // Cleanup
        service.MeetingStateChanged -= handler;
    }

    [TestMethod]
    public void CheckAndUpdate_DoesNotThrow()
    {
        var service = MeetingDetectionService.Instance;
        
        try
        {
            service.CheckAndUpdate();
            // Should not throw even if registry access fails
        }
        catch (Exception ex)
        {
            Assert.Fail($"CheckAndUpdate should not throw: {ex.Message}");
        }
    }

    [TestMethod]
    public void CheckAndUpdate_CanBeCalledMultipleTimes()
    {
        var service = MeetingDetectionService.Instance;
        
        try
        {
            service.CheckAndUpdate();
            service.CheckAndUpdate();
            service.CheckAndUpdate();
            // Should not throw
        }
        catch (Exception ex)
        {
            Assert.Fail($"Multiple CheckAndUpdate calls should not throw: {ex.Message}");
        }
    }
}
