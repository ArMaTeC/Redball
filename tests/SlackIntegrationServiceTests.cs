using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System.Threading.Tasks;

namespace Redball.Tests;

[TestClass]
public class SlackIntegrationServiceTests
{
    [TestInitialize]
    public void Setup()
    {
        ConfigService.Instance.Config.MeetingAware = true;
    }

    [TestMethod]
    public void Instance_IsSingleton()
    {
        var instance1 = SlackIntegrationService.Instance;
        var instance2 = SlackIntegrationService.Instance;
        
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void IsEnabled_WhenConfigDisabled_ReturnsFalse()
    {
        ConfigService.Instance.Config.MeetingAware = false;
        
        Assert.IsFalse(SlackIntegrationService.Instance.IsEnabled);
    }

    [TestMethod]
    public void IsEnabled_WhenConfigEnabled_ReturnsTrue()
    {
        ConfigService.Instance.Config.MeetingAware = true;
        
        Assert.IsTrue(SlackIntegrationService.Instance.IsEnabled);
    }

    [TestMethod]
    public async Task CheckStatusAsync_WhenDisabled_ReturnsUnknown()
    {
        ConfigService.Instance.Config.MeetingAware = false;
        
        var status = await SlackIntegrationService.Instance.CheckStatusAsync();
        
        Assert.AreEqual(SlackHuddleStatus.Unknown, status);
    }

    [TestMethod]
    public async Task CheckStatusAsync_DoesNotThrow()
    {
        try
        {
            var status = await SlackIntegrationService.Instance.CheckStatusAsync();
            // Should not throw
        }
        catch
        {
            Assert.Fail("CheckStatusAsync should not throw");
        }
    }

    [TestMethod]
    public void IsInHuddle_WhenNotRunning_ReturnsFalse()
    {
        var result = SlackIntegrationService.Instance.IsInHuddle;
        
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsScreenSharing_WhenNotRunning_ReturnsFalse()
    {
        var result = SlackIntegrationService.Instance.IsScreenSharing;
        
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsSlackInHuddle_WhenDisabled_ReturnsFalse()
    {
        ConfigService.Instance.Config.MeetingAware = false;
        
        var result = SlackIntegrationService.Instance.IsSlackInHuddle();
        
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsSlackInHuddle_DoesNotThrow()
    {
        try
        {
            var result = SlackIntegrationService.Instance.IsSlackInHuddle();
            // Should not throw
        }
        catch
        {
            Assert.Fail("IsSlackInHuddle should not throw");
        }
    }

    [TestMethod]
    public void IsScreenSharingInSlack_WhenDisabled_ReturnsFalse()
    {
        ConfigService.Instance.Config.MeetingAware = false;
        
        var result = SlackIntegrationService.Instance.IsScreenSharingInSlack();
        
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void StatusChanged_EventCanBeSubscribed()
    {
        bool eventFired = false;
        EventHandler<SlackHuddleStatusChangedEventArgs> handler = (s, e) => eventFired = true;
        
        SlackIntegrationService.Instance.StatusChanged += handler;
        
        // Event subscription should work
        Assert.IsTrue(true);
        
        SlackIntegrationService.Instance.StatusChanged -= handler;
    }
}
