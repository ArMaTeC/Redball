using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI;
using Redball.UI.Services;

namespace Redball.Tests;

[TestClass]
public class MiniWidgetPresetServiceTests
{
    [TestMethod]
    public void NormalizePreset_Focus_ReturnsFocus()
    {
        var result = MiniWidgetPresetService.NormalizePreset("Focus");
        Assert.AreEqual(MiniWidgetPresetService.Focus, result);
    }

    [TestMethod]
    public void NormalizePreset_Meeting_ReturnsMeeting()
    {
        var result = MiniWidgetPresetService.NormalizePreset("Meeting");
        Assert.AreEqual(MiniWidgetPresetService.Meeting, result);
    }

    [TestMethod]
    public void NormalizePreset_BatterySafe_ReturnsBatterySafe()
    {
        var result = MiniWidgetPresetService.NormalizePreset("BatterySafe");
        Assert.AreEqual(MiniWidgetPresetService.BatterySafe, result);
    }

    [TestMethod]
    public void NormalizePreset_Custom_ReturnsCustom()
    {
        var result = MiniWidgetPresetService.NormalizePreset("Custom");
        Assert.AreEqual(MiniWidgetPresetService.Custom, result);
    }

    [TestMethod]
    public void NormalizePreset_Null_ReturnsCustom()
    {
        var result = MiniWidgetPresetService.NormalizePreset(null);
        Assert.AreEqual(MiniWidgetPresetService.Custom, result);
    }

    [TestMethod]
    public void NormalizePreset_EmptyString_ReturnsCustom()
    {
        var result = MiniWidgetPresetService.NormalizePreset("");
        Assert.AreEqual(MiniWidgetPresetService.Custom, result);
    }

    [TestMethod]
    public void NormalizePreset_UnknownValue_ReturnsCustom()
    {
        var result = MiniWidgetPresetService.NormalizePreset("UnknownValue");
        Assert.AreEqual(MiniWidgetPresetService.Custom, result);
    }

    [TestMethod]
    public void NormalizePreset_CaseInsensitive_Focus()
    {
        var result = MiniWidgetPresetService.NormalizePreset("focus");
        Assert.AreEqual(MiniWidgetPresetService.Focus, result);
    }

    [TestMethod]
    public void NormalizePreset_CaseInsensitive_MEETING()
    {
        var result = MiniWidgetPresetService.NormalizePreset("MEETING");
        Assert.AreEqual(MiniWidgetPresetService.Meeting, result);
    }

    [TestMethod]
    public void NormalizePreset_CaseInsensitive_BatterySafe_MixedCase()
    {
        var result = MiniWidgetPresetService.NormalizePreset("BatterySAFE");
        Assert.AreEqual(MiniWidgetPresetService.BatterySafe, result);
    }

    [TestMethod]
    public void ApplyPreset_Focus_SetsCorrectConfigValues()
    {
        var config = new RedballConfig();
        
        MiniWidgetPresetService.ApplyPreset(config, "Focus");

        Assert.AreEqual(MiniWidgetPresetService.Focus, config.MiniWidgetPreset);
        Assert.IsTrue(config.MiniWidgetAlwaysOnTop);
        Assert.AreEqual(95, config.MiniWidgetOpacityPercent);
        Assert.IsTrue(config.MiniWidgetShowQuickActions);
        Assert.IsFalse(config.MiniWidgetShowStatusIcons);
        Assert.IsTrue(config.MiniWidgetDoubleClickOpensDashboard);
        Assert.IsTrue(config.MiniWidgetOpenOnStartup);
        Assert.IsFalse(config.MiniWidgetLockPosition);
        Assert.IsTrue(config.MiniWidgetSnapToScreenEdges);
        Assert.IsTrue(config.MiniWidgetEnableKeyboardShortcuts);
        Assert.AreEqual(25, config.MiniWidgetCustomQuickMinutes);
        Assert.IsTrue(config.MiniWidgetConfirmCloseWhenActive);
    }

    [TestMethod]
    public void ApplyPreset_Meeting_SetsCorrectConfigValues()
    {
        var config = new RedballConfig();
        
        MiniWidgetPresetService.ApplyPreset(config, "Meeting");

        Assert.AreEqual(MiniWidgetPresetService.Meeting, config.MiniWidgetPreset);
        Assert.IsTrue(config.MiniWidgetAlwaysOnTop);
        Assert.AreEqual(90, config.MiniWidgetOpacityPercent);
        Assert.IsFalse(config.MiniWidgetShowQuickActions);
        Assert.IsTrue(config.MiniWidgetShowStatusIcons);
        Assert.IsFalse(config.MiniWidgetDoubleClickOpensDashboard);
        Assert.IsTrue(config.MiniWidgetOpenOnStartup);
        Assert.IsTrue(config.MiniWidgetLockPosition);
        Assert.IsTrue(config.MiniWidgetSnapToScreenEdges);
        Assert.IsFalse(config.MiniWidgetEnableKeyboardShortcuts);
        Assert.AreEqual(60, config.MiniWidgetCustomQuickMinutes);
        Assert.IsTrue(config.MiniWidgetConfirmCloseWhenActive);
    }

    [TestMethod]
    public void ApplyPreset_BatterySafe_SetsCorrectConfigValues()
    {
        var config = new RedballConfig();
        
        MiniWidgetPresetService.ApplyPreset(config, "BatterySafe");

        Assert.AreEqual(MiniWidgetPresetService.BatterySafe, config.MiniWidgetPreset);
        Assert.IsFalse(config.MiniWidgetAlwaysOnTop);
        Assert.AreEqual(88, config.MiniWidgetOpacityPercent);
        Assert.IsTrue(config.MiniWidgetShowQuickActions);
        Assert.IsTrue(config.MiniWidgetShowStatusIcons);
        Assert.IsTrue(config.MiniWidgetDoubleClickOpensDashboard);
        Assert.IsFalse(config.MiniWidgetOpenOnStartup);
        Assert.IsFalse(config.MiniWidgetLockPosition);
        Assert.IsTrue(config.MiniWidgetSnapToScreenEdges);
        Assert.IsTrue(config.MiniWidgetEnableKeyboardShortcuts);
        Assert.AreEqual(15, config.MiniWidgetCustomQuickMinutes);
        Assert.IsTrue(config.MiniWidgetConfirmCloseWhenActive);
    }

    [TestMethod]
    public void ApplyPreset_Custom_DoesNotModifyConfig()
    {
        var config = new RedballConfig();
        var originalAlwaysOnTop = config.MiniWidgetAlwaysOnTop;
        var originalOpacity = config.MiniWidgetOpacityPercent;
        
        MiniWidgetPresetService.ApplyPreset(config, "Custom");

        Assert.AreEqual(MiniWidgetPresetService.Custom, config.MiniWidgetPreset);
        // Custom preset should not modify other settings
        Assert.AreEqual(originalAlwaysOnTop, config.MiniWidgetAlwaysOnTop);
        Assert.AreEqual(originalOpacity, config.MiniWidgetOpacityPercent);
    }

    [TestMethod]
    public void ApplyPreset_InvalidPreset_TreatsAsCustom()
    {
        var config = new RedballConfig();
        var originalPreset = config.MiniWidgetPreset;
        
        MiniWidgetPresetService.ApplyPreset(config, "InvalidPreset");

        Assert.AreEqual(MiniWidgetPresetService.Custom, config.MiniWidgetPreset);
    }

    [TestMethod]
    public void ApplyPreset_FromFocusToMeeting_ChangesValues()
    {
        var config = new RedballConfig();
        
        // First apply Focus
        MiniWidgetPresetService.ApplyPreset(config, "Focus");
        Assert.AreEqual(95, config.MiniWidgetOpacityPercent);
        Assert.IsTrue(config.MiniWidgetShowQuickActions);

        // Then apply Meeting
        MiniWidgetPresetService.ApplyPreset(config, "Meeting");
        Assert.AreEqual(90, config.MiniWidgetOpacityPercent);
        Assert.IsFalse(config.MiniWidgetShowQuickActions);
        Assert.AreEqual(MiniWidgetPresetService.Meeting, config.MiniWidgetPreset);
    }

    [TestMethod]
    public void ApplyPreset_FromBatterySafeToFocus_ChangesValues()
    {
        var config = new RedballConfig();
        
        // First apply BatterySafe
        MiniWidgetPresetService.ApplyPreset(config, "BatterySafe");
        Assert.IsFalse(config.MiniWidgetAlwaysOnTop);
        Assert.AreEqual(15, config.MiniWidgetCustomQuickMinutes);

        // Then apply Focus
        MiniWidgetPresetService.ApplyPreset(config, "Focus");
        Assert.IsTrue(config.MiniWidgetAlwaysOnTop);
        Assert.AreEqual(25, config.MiniWidgetCustomQuickMinutes);
        Assert.AreEqual(MiniWidgetPresetService.Focus, config.MiniWidgetPreset);
    }

    [TestMethod]
    public void Constants_AreCorrectValues()
    {
        Assert.AreEqual("Custom", MiniWidgetPresetService.Custom);
        Assert.AreEqual("Focus", MiniWidgetPresetService.Focus);
        Assert.AreEqual("Meeting", MiniWidgetPresetService.Meeting);
        Assert.AreEqual("BatterySafe", MiniWidgetPresetService.BatterySafe);
    }
}
