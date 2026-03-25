using System;

namespace Redball.UI.Services;

public static class MiniWidgetPresetService
{
    public const string Custom = "Custom";
    public const string Focus = "Focus";
    public const string Meeting = "Meeting";
    public const string BatterySafe = "BatterySafe";

    public static string NormalizePreset(string? preset)
    {
        if (string.Equals(preset, Focus, StringComparison.OrdinalIgnoreCase))
            return Focus;

        if (string.Equals(preset, Meeting, StringComparison.OrdinalIgnoreCase))
            return Meeting;

        if (string.Equals(preset, BatterySafe, StringComparison.OrdinalIgnoreCase))
            return BatterySafe;

        return Custom;
    }

    public static void ApplyPreset(RedballConfig config, string? preset)
    {
        var normalized = NormalizePreset(preset);

        switch (normalized)
        {
            case Focus:
                config.MiniWidgetAlwaysOnTop = true;
                config.MiniWidgetOpacityPercent = 95;
                config.MiniWidgetShowQuickActions = true;
                config.MiniWidgetShowStatusIcons = false;
                config.MiniWidgetDoubleClickOpensDashboard = true;
                config.MiniWidgetOpenOnStartup = true;
                config.MiniWidgetLockPosition = false;
                config.MiniWidgetSnapToScreenEdges = true;
                config.MiniWidgetEnableKeyboardShortcuts = true;
                config.MiniWidgetCustomQuickMinutes = 25;
                config.MiniWidgetConfirmCloseWhenActive = true;
                break;
            case Meeting:
                config.MiniWidgetAlwaysOnTop = true;
                config.MiniWidgetOpacityPercent = 90;
                config.MiniWidgetShowQuickActions = false;
                config.MiniWidgetShowStatusIcons = true;
                config.MiniWidgetDoubleClickOpensDashboard = false;
                config.MiniWidgetOpenOnStartup = true;
                config.MiniWidgetLockPosition = true;
                config.MiniWidgetSnapToScreenEdges = true;
                config.MiniWidgetEnableKeyboardShortcuts = false;
                config.MiniWidgetCustomQuickMinutes = 60;
                config.MiniWidgetConfirmCloseWhenActive = true;
                break;
            case BatterySafe:
                config.MiniWidgetAlwaysOnTop = false;
                config.MiniWidgetOpacityPercent = 88;
                config.MiniWidgetShowQuickActions = true;
                config.MiniWidgetShowStatusIcons = true;
                config.MiniWidgetDoubleClickOpensDashboard = true;
                config.MiniWidgetOpenOnStartup = false;
                config.MiniWidgetLockPosition = false;
                config.MiniWidgetSnapToScreenEdges = true;
                config.MiniWidgetEnableKeyboardShortcuts = true;
                config.MiniWidgetCustomQuickMinutes = 15;
                config.MiniWidgetConfirmCloseWhenActive = true;
                break;
        }

        config.MiniWidgetPreset = normalized;
    }
}
