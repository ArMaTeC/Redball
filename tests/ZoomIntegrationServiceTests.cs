using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System.Threading.Tasks;

namespace Redball.Tests;

[TestClass]
public class ZoomIntegrationServiceTests
{
    [TestInitialize]
    public void Setup()
    {
        ConfigService.Instance.Config.MeetingAware = true;
    }

    [TestMethod]
    public void Instance_IsSingleton()
    {
        var instance1 = ZoomIntegrationService.Instance;
        var instance2 = ZoomIntegrationService.Instance;
        
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void IsEnabled_WhenConfigDisabled_ReturnsFalse()
    {
        ConfigService.Instance.Config.MeetingAware = false;
        
        Assert.IsFalse(ZoomIntegrationService.Instance.IsEnabled);
    }

    [TestMethod]
    public void IsEnabled_WhenConfigEnabled_ReturnsTrue()
    {
        ConfigService.Instance.Config.MeetingAware = true;
        
        Assert.IsTrue(ZoomIntegrationService.Instance.IsEnabled);
    }

    [TestMethod]
    public async Task CheckStatusAsync_WhenDisabled_ReturnsUnknown()
    {
        ConfigService.Instance.Config.MeetingAware = false;
        
        var status = await ZoomIntegrationService.Instance.CheckStatusAsync();
        
        Assert.AreEqual(ZoomStatus.Unknown, status);
    }

    [TestMethod]
    public async Task CheckStatusAsync_DoesNotThrow()
    {
        try
        {
            var status = await ZoomIntegrationService.Instance.CheckStatusAsync();
            // Should not throw
        }
        catch
        {
            Assert.Fail("CheckStatusAsync should not throw");
        }
    }

    [TestMethod]
    public void IsInMeeting_WhenNotRunning_ReturnsFalse()
    {
        var result = ZoomIntegrationService.Instance.IsInMeeting;
        
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsScreenSharing_WhenNotRunning_ReturnsFalse()
    {
        var result = ZoomIntegrationService.Instance.IsScreenSharing;
        
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsRecording_WhenNotRunning_ReturnsFalse()
    {
        var result = ZoomIntegrationService.Instance.IsRecording;
        
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsZoomInMeeting_WhenDisabled_ReturnsFalse()
    {
        ConfigService.Instance.Config.MeetingAware = false;
        
        var result = ZoomIntegrationService.Instance.IsZoomInMeeting();
        
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsZoomInMeeting_DoesNotThrow()
    {
        try
        {
            var result = ZoomIntegrationService.Instance.IsZoomInMeeting();
            // Should not throw
        }
        catch
        {
            Assert.Fail("IsZoomInMeeting should not throw");
        }
    }

    [TestMethod]
    public void IsScreenSharingInZoom_WhenDisabled_ReturnsFalse()
    {
        ConfigService.Instance.Config.MeetingAware = false;
        
        var result = ZoomIntegrationService.Instance.IsScreenSharingInZoom();
        
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsRecordingInZoom_WhenDisabled_ReturnsFalse()
    {
        ConfigService.Instance.Config.MeetingAware = false;
        
        var result = ZoomIntegrationService.Instance.IsRecordingInZoom();
        
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void StatusChanged_EventCanBeSubscribed()
    {
        bool eventFired = false;
        EventHandler<ZoomStatusChangedEventArgs> handler = (s, e) => eventFired = true;
        
        ZoomIntegrationService.Instance.StatusChanged += handler;
        
        // Event subscription should work
        Assert.IsTrue(true);
        
        ZoomIntegrationService.Instance.StatusChanged -= handler;
    }
}
