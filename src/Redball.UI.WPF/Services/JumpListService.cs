using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Shell;

namespace Redball.UI.Services;

/// <summary>
/// Native OS Integration (7.1): Windows 11 Jump Lists integration.
/// Maps direct application shortcuts (TypeThing, KeepAwake Toggle, Settings) to the Windows Taskbar right-click menu.
/// </summary>
public static class JumpListService
{
    public static void Initialize()
    {
        try
        {
            var exePath = Assembly.GetExecutingAssembly().Location;
            // On .NET single file deployments, Location can be empty
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = Environment.GetCommandLineArgs()[0];
            }

            var jumpList = new JumpList();
            
            // Task 1: TypeThing Immediate Start
            var typeTask = new JumpTask
            {
                Title = "Start TypeThing",
                Description = "Immediately reads your clipboard and begins typing.",
                CustomCategory = "Actions",
                ApplicationPath = exePath,
                Arguments = "--typething-start",
                IconResourcePath = exePath,
                IconResourceIndex = 0
            };

            // Task 2: Keep-Awake Toggle
            var awakeTask = new JumpTask
            {
                Title = "Toggle Keep-Awake",
                Description = "Start or stop the background keep-awake monitor.",
                CustomCategory = "Actions",
                ApplicationPath = exePath,
                Arguments = "--toggle-keepawake",
                IconResourcePath = exePath,
                IconResourceIndex = 0
            };

            // Task 3: Settings
            var settingsTask = new JumpTask
            {
                Title = "Settings",
                Description = "Open Redball Configuration",
                CustomCategory = "System",
                ApplicationPath = exePath,
                Arguments = "--settings",
                IconResourcePath = "shell32.dll",
                IconResourceIndex = 314
            };

            jumpList.JumpItems.Add(typeTask);
            jumpList.JumpItems.Add(awakeTask);
            jumpList.JumpItems.Add(settingsTask);

            jumpList.ShowFrequentCategory = false;
            jumpList.ShowRecentCategory = false;

            JumpList.SetJumpList(Application.Current, jumpList);
            jumpList.Apply();
            
            Logger.Info("JumpListService", "Windows Taskbar JumpLists natively integrated.");
        }
        catch (Exception ex)
        {
            Logger.Error("JumpListService", "Failed to construct native JumpLists.", ex);
        }
    }
}
