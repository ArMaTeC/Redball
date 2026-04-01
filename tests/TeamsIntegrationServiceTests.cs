using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System.Threading.Tasks;

namespace Redball.Tests;

[TestClass]
public class TeamsIntegrationServiceTests
{
    [TestInitialize]
    public void Setup()
    {
        ConfigService.Instance.Config.MeetingAware = true;
    }

    [TestMethod]
    public void Instance_IsSingleton()
    {
        var instance1 = TeamsIntegrationService.Instance;
        var instance2 = TeamsIntegrationService.Instance;
        
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void IsEnabled_WhenConfigDisabled_ReturnsFalse()
    {
        ConfigService.Instance.Config.MeetingAware = false;
        
        Assert.IsFalse(TeamsIntegrationService.Instance.IsEnabled);
    }

    [TestMethod]
    public void IsEnabled_WhenConfigEnabled_ReturnsTrue()
    {
        ConfigService.Instance.Config.MeetingAware = true;
        
        Assert.IsTrue(TeamsIntegrationService.Instance.IsEnabled);
    }

    [TestMethod]
    public async Task CheckStatusAsync_WhenDisabled_ReturnsUnknown()
    {
        ConfigService.Instance.Config.MeetingAware = false;
        
        var status = await TeamsIntegrationService.Instance.CheckStatusAsync();
        
        Assert.AreEqual(TeamsStatus.Unknown, status);
    }

    [TestMethod]
    public async Task CheckStatusAsync_DoesNotThrow()
    {
        try
        {
            var status = await TeamsIntegrationService.Instance.CheckStatusAsync();
            // Should not throw
        }
        catch
        {
            Assert.Fail("CheckStatusAsync should not throw");
        }
    }

    [TestMethod]
    public void IsInCall_WhenNotRunning_ReturnsFalse()
    {
        // Status would be Unknown or NotRunning
        var result = TeamsIntegrationService.Instance.IsInCall;
        
        // Should be false when not in a call
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsPresenting_WhenNotRunning_ReturnsFalse()
    {
        var result = TeamsIntegrationService.Instance.IsPresenting;
        
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsTeamsInMeeting_WhenDisabled_ReturnsFalse()
    {
        ConfigService.Instance.Config.MeetingAware = false;
        
        var result = TeamsIntegrationService.Instance.IsTeamsInMeeting();
        
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsTeamsInMeeting_DoesNotThrow()
    {
        try
        {
            var result = TeamsIntegrationService.Instance.IsTeamsInMeeting();
            // Should not throw
        }
        catch
        {
            Assert.Fail("IsTeamsInMeeting should not throw");
        }
    }

    [TestMethod]
    public void IsScreenSharing_WhenDisabled_ReturnsFalse()
    {
        ConfigService.Instance.Config.MeetingAware = false;
        
        var result = TeamsIntegrationService.Instance.IsScreenSharing();
        
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void StatusChanged_EventCanBeSubscribed()
    {
        bool eventFired = false;
        EventHandler<TeamsStatusChangedEventArgs> handler = (s, e) => eventFired = true;
        
        TeamsIntegrationService.Instance.StatusChanged += handler;
        
        // Event subscription should work
        Assert.IsTrue(true);
        
        TeamsIntegrationService.Instance.StatusChanged -= handler;
        
        // Use eventFired to suppress warning - we're testing subscription only
        Assert.IsFalse(eventFired);
    }
}
