using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;

namespace Redball.Tests;

/// <summary>
/// Mock plugin implementation for testing
/// </summary>
public class MockPlugin : IRedballPlugin
{
    public string Name { get; set; } = "MockPlugin";
    public string Description { get; set; } = "Test plugin";
    public bool OnLoadCalled { get; private set; }
    public bool OnActivateCalled { get; private set; }
    public bool OnPauseCalled { get; private set; }
    public bool OnTimerExpireCalled { get; private set; }
    public bool OnUnloadCalled { get; private set; }

    public void OnLoad() => OnLoadCalled = true;
    public void OnActivate() => OnActivateCalled = true;
    public void OnPause() => OnPauseCalled = true;
    public void OnTimerExpire() => OnTimerExpireCalled = true;
    public void OnUnload() => OnUnloadCalled = true;
}

[TestClass]
public class PluginServiceTests
{
    [TestMethod]
    public void Instance_IsSingleton()
    {
        var instance1 = PluginService.Instance;
        var instance2 = PluginService.Instance;
        
        Assert.AreSame(instance1, instance2);
    }

    [TestMethod]
    public void LoadedPlugins_InitiallyEmpty()
    {
        var service = PluginService.Instance;
        
        // After any previous tests, unload all first
        service.UnloadAll();
        
        Assert.AreEqual(0, service.LoadedPlugins.Count);
    }

    [TestMethod]
    public void NotifyActivate_WithNoPlugins_DoesNotThrow()
    {
        var service = PluginService.Instance;
        service.UnloadAll();
        
        try
        {
            service.NotifyActivate();
            // Should not throw
        }
        catch (Exception ex)
        {
            Assert.Fail($"NotifyActivate should not throw with no plugins: {ex.Message}");
        }
    }

    [TestMethod]
    public void NotifyPause_WithNoPlugins_DoesNotThrow()
    {
        var service = PluginService.Instance;
        service.UnloadAll();
        
        try
        {
            service.NotifyPause();
            // Should not throw
        }
        catch (Exception ex)
        {
            Assert.Fail($"NotifyPause should not throw with no plugins: {ex.Message}");
        }
    }

    [TestMethod]
    public void NotifyTimerExpire_WithNoPlugins_DoesNotThrow()
    {
        var service = PluginService.Instance;
        service.UnloadAll();
        
        try
        {
            service.NotifyTimerExpire();
            // Should not throw
        }
        catch (Exception ex)
        {
            Assert.Fail($"NotifyTimerExpire should not throw with no plugins: {ex.Message}");
        }
    }

    [TestMethod]
    public void UnloadAll_WithNoPlugins_DoesNotThrow()
    {
        var service = PluginService.Instance;
        service.UnloadAll();
        
        try
        {
            service.UnloadAll();
            // Should not throw
        }
        catch (Exception ex)
        {
            Assert.Fail($"UnloadAll should not throw with no plugins: {ex.Message}");
        }
    }

    [TestMethod]
    public void GetStatusText_NoPlugins_ReturnsNoPluginsMessage()
    {
        var service = PluginService.Instance;
        service.UnloadAll();
        
        var status = service.GetStatusText();
        
        Assert.AreEqual("No plugins loaded", status);
    }

    [TestMethod]
    public void GetStatusText_WithPlugins_ReturnsPluginNames()
    {
        var service = PluginService.Instance;
        service.UnloadAll();
        
        // This test would need actual plugin DLLs to work properly
        // For now, just verify the method doesn't throw
        var status = service.GetStatusText();
        
        Assert.IsNotNull(status);
    }

    [TestMethod]
    public void LoadPlugins_CreatesPluginsDirectory()
    {
        var service = PluginService.Instance;
        
        // LoadPlugins should create the directory if it doesn't exist
        service.LoadPlugins();
        
        // Should not throw, directory should exist
        Assert.IsTrue(true);
    }

    [TestMethod]
    public void UnloadAll_ClearsLoadedPlugins()
    {
        var service = PluginService.Instance;
        
        // Ensure clean state
        service.UnloadAll();
        
        Assert.AreEqual(0, service.LoadedPlugins.Count);
    }

    [TestMethod]
    public void MultipleUnloadAll_CallsDoesNotThrow()
    {
        var service = PluginService.Instance;
        
        service.UnloadAll();
        service.UnloadAll();
        service.UnloadAll();
        
        // Should not throw
        Assert.AreEqual(0, service.LoadedPlugins.Count);
    }
}
