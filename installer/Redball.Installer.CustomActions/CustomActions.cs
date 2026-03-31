using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using WixToolset.Dtf.WindowsInstaller;

namespace Redball.Installer
{
    public class CustomActions
    {
        private static readonly string LogFileName = "Redball_Install_Log.txt";
        private static string? _sessionLogPath;

        [CustomAction]
        public static ActionResult ApplyEnterpriseConfig(Session session)
        {
            try
            {
                LogMessage(session, "=== Applying Enterprise Configuration ===");
                
                var isSilent = session["REDBALL_SILENTINSTALL"] == "1";
                if (isSilent)
                {
                    LogMessage(session, "Silent installation mode detected.");
                }
                
                // Determine registry root based on install scope
                var isPerMachine = session["ALLUSERS"] == "1";
                var registryRoot = isPerMachine ? Registry.LocalMachine : Registry.CurrentUser;
                var policyKey = session["REDBALL_POLICYREGISTRY"] ?? @"SOFTWARE\Policies\ArMaTeC\Redball";
                var defaultsKey = @"SOFTWARE\ArMaTeC\Redball\Defaults";
                
                // Apply enterprise settings if provided
                ApplyRegistrySetting(session, registryRoot, policyKey, "StartMinimized", "REDBALL_STARTMINIMIZED");
                ApplyRegistrySetting(session, registryRoot, policyKey, "BatteryAware", "REDBALL_ENABLEBATTERYAWARE");
                ApplyRegistrySetting(session, registryRoot, policyKey, "NetworkAware", "REDBALL_ENABLENETWORKAWARE");
                ApplyRegistrySetting(session, registryRoot, policyKey, "IdleDetection", "REDBALL_ENABLEIDLEDETECTION");
                ApplyRegistrySetting(session, registryRoot, policyKey, "InstallHID", "REDBALL_INSTALLHID");
                ApplyRegistrySetting(session, registryRoot, policyKey, "DisableDesktopShortcut", "REDBALL_DISABLEDESKTOPSHORTCUT");
                ApplyRegistrySetting(session, registryRoot, policyKey, "DisableStartup", "REDBALL_DISABLESTARTUP");
                ApplyRegistrySetting(session, registryRoot, policyKey, "EnableTelemetry", "REDBALL_ENABLETELEMETRY");
                ApplyRegistrySetting(session, registryRoot, policyKey, "ConfigEncrypted", "REDBALL_CONFIGENCRYPTED");
                
                // Write defaults to user-level registry (even for per-machine, defaults go to HKCU for current user)
                using (var key = Registry.CurrentUser.CreateSubKey(defaultsKey))
                {
                    if (key != null)
                    {
                        key.SetValue("EnterpriseManaged", isSilent || IsAnyEnterpriseSettingSet(session) ? 1 : 0);
                        key.SetValue("AppliedDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        LogMessage(session, "Enterprise defaults recorded in registry.");
                    }
                }
                
                LogMessage(session, "Enterprise configuration applied successfully.");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                LogMessage(session, $"ERROR applying enterprise config: {ex.Message}");
                return ActionResult.Success; // Continue installation even if enterprise config fails
            }
        }
        
        private static void ApplyRegistrySetting(Session session, RegistryKey root, string keyPath, string valueName, string propertyName)
        {
            var value = session[propertyName];
            if (!string.IsNullOrEmpty(value))
            {
                try
                {
                    using var key = root.CreateSubKey(keyPath);
                    if (key != null)
                    {
                        // Convert string "1"/"0" to integer
                        if (int.TryParse(value, out var intValue))
                        {
                            key.SetValue(valueName, intValue, RegistryValueKind.DWord);
                        }
                        else
                        {
                            key.SetValue(valueName, value);
                        }
                        LogMessage(session, $"  Set {valueName} = {value}");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage(session, $"  Warning: Could not set {valueName}: {ex.Message}");
                }
            }
        }
        
        private static bool IsAnyEnterpriseSettingSet(Session session)
        {
            var props = new[] { 
                "REDBALL_STARTMINIMIZED", "REDBALL_ENABLEBATTERYAWARE", 
                "REDBALL_ENABLENETWORKAWARE", "REDBALL_ENABLEIDLEDETECTION",
                "REDBALL_INSTALLHID", "REDBALL_DISABLEDESKTOPSHORTCUT",
                "REDBALL_DISABLESTARTUP", "REDBALL_ENABLETELEMETRY",
                "REDBALL_CONFIGENCRYPTED"
            };
            return props.Any(p => !string.IsNullOrEmpty(session[p]));
        }

        [CustomAction]
        public static ActionResult InitializeLogging(Session session)
        {
            try
            {
                var tempPath = Path.GetTempPath();
                _sessionLogPath = Path.Combine(tempPath, LogFileName);
                
                LogMessage(session, "=== Redball Installer Log Started ===");
                LogMessage(session, $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                LogMessage(session, $"Installer Version: {session["ProductVersion"]}");
                LogMessage(session, $"Product Code: {session["ProductCode"]}");
                LogMessage(session, $"Install Mode: {(session["REMOVE"] == "ALL" ? "Uninstall" : session["UPGRADINGPRODUCTCODE"] != "" ? "Upgrade" : "Install")}");
                
                session["REDBALL_LOGPATH"] = _sessionLogPath;
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"ERROR in InitializeLogging: {ex}");
                return ActionResult.Success;
            }
        }

        [CustomAction]
        public static ActionResult BackupUserData(Session session)
        {
            try
            {
                LogMessage(session, "Starting user data backup...");
                
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var userDataPath = Path.Combine(localAppData, "Redball", "UserData");
                var backupPath = Path.Combine(Path.GetTempPath(), $"RedballBackup_{DateTime.Now:yyyyMMddHHmmss}");
                
                if (!Directory.Exists(userDataPath))
                {
                    LogMessage(session, "No existing user data found to backup.");
                    session["REDBALL_BACKUP_PATH"] = "";
                    return ActionResult.Success;
                }
                
                LogMessage(session, $"Backing up from: {userDataPath}");
                LogMessage(session, $"Backup destination: {backupPath}");
                
                Directory.CreateDirectory(backupPath);
                
                var sourceDir = new DirectoryInfo(userDataPath);
                CopyDirectoryRecursive(sourceDir, backupPath, session);
                
                session["REDBALL_BACKUP_PATH"] = backupPath;
                LogMessage(session, "User data backup completed successfully.");
                
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                LogMessage(session, $"ERROR during backup: {ex.Message}");
                session.Log($"Backup failed: {ex}");
                return ActionResult.Success;
            }
        }

        [CustomAction]
        public static ActionResult RestoreUserData(Session session)
        {
            try
            {
                var backupPath = session["REDBALL_BACKUP_PATH"];
                if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath))
                {
                    LogMessage(session, "No backup to restore.");
                    return ActionResult.Success;
                }
                
                LogMessage(session, "Starting user data restore...");
                
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var userDataPath = Path.Combine(localAppData, "Redball", "UserData");
                
                Directory.CreateDirectory(Path.GetDirectoryName(userDataPath)!);
                
                LogMessage(session, $"Restoring from: {backupPath}");
                LogMessage(session, $"Restoring to: {userDataPath}");
                
                var backupDir = new DirectoryInfo(backupPath);
                CopyDirectoryRecursive(backupDir, userDataPath, session);
                
                try
                {
                    Directory.Delete(backupPath, true);
                    LogMessage(session, "Cleaned up backup files.");
                }
                catch
                {
                    LogMessage(session, "Note: Could not clean up backup files (non-critical).");
                }
                
                LogMessage(session, "User data restore completed successfully.");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                LogMessage(session, $"ERROR during restore: {ex.Message}");
                session.Log($"Restore failed: {ex}");
                return ActionResult.Success;
            }
        }

        [CustomAction]
        public static ActionResult StopRunningProcesses(Session session)
        {
            try
            {
                LogMessage(session, "Checking for running Redball processes...");
                
                var processNames = new[] { "Redball.UI.WPF", "Redball", "Redball.E2E.Tests" };
                var stoppedCount = 0;
                
                foreach (var procName in processNames)
                {
                    var processes = Process.GetProcessesByName(procName);
                    foreach (var proc in processes)
                    {
                        try
                        {
                            LogMessage(session, $"Stopping process: {proc.ProcessName} (PID: {proc.Id})");
                            proc.Kill();
                            proc.WaitForExit(5000);
                            stoppedCount++;
                        }
                        catch (Exception ex)
                        {
                            LogMessage(session, $"Warning: Could not stop {proc.ProcessName}: {ex.Message}");
                        }
                    }
                }
                
                if (stoppedCount > 0)
                {
                    LogMessage(session, $"Stopped {stoppedCount} process(es).");
                    Thread.Sleep(1000);
                }
                else
                {
                    LogMessage(session, "No running processes found.");
                }
                
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                LogMessage(session, $"Warning during process cleanup: {ex.Message}");
                return ActionResult.Success;
            }
        }

        [CustomAction]
        public static ActionResult CleanupOrphanedFiles(Session session)
        {
            try
            {
                LogMessage(session, "Starting cleanup of misplaced installation files...");
                
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var installFolder = Path.Combine(localAppData, "Redball");
                
                if (!Directory.Exists(installFolder))
                {
                    LogMessage(session, "Install folder not found, skipping cleanup.");
                    return ActionResult.Success;
                }
                
                // Files that should be in dll/ subfolder (not root)
                var dllFilesToRemove = new[]
                {
                    "CommunityToolkit.Mvvm.dll",
                    "e_sqlite3.dll",
                    "Hardcodet.NotifyIcon.Wpf.dll",
                    "libSkiaSharp.dll",
                    "LottieSharp.dll",
                    "Microsoft.Data.Sqlite.dll",
                    "Microsoft.Extensions.Configuration.Abstractions.dll",
                    "Microsoft.Extensions.Configuration.Binder.dll",
                    "Microsoft.Extensions.Configuration.CommandLine.dll",
                    "Microsoft.Extensions.Configuration.dll",
                    "Microsoft.Extensions.Configuration.EnvironmentVariables.dll",
                    "Microsoft.Extensions.Configuration.FileExtensions.dll",
                    "Microsoft.Extensions.Configuration.Json.dll",
                    "Microsoft.Extensions.Configuration.UserSecrets.dll",
                    "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
                    "Microsoft.Extensions.DependencyInjection.dll",
                    "Microsoft.Extensions.Diagnostics.Abstractions.dll",
                    "Microsoft.Extensions.Diagnostics.dll",
                    "Microsoft.Extensions.FileProviders.Abstractions.dll",
                    "Microsoft.Extensions.FileProviders.Physical.dll",
                    "Microsoft.Extensions.FileSystemGlobbing.dll",
                    "Microsoft.Extensions.Hosting.Abstractions.dll",
                    "Microsoft.Extensions.Hosting.dll",
                    "Microsoft.Extensions.Http.dll",
                    "Microsoft.Extensions.Logging.Abstractions.dll",
                    "Microsoft.Extensions.Logging.Configuration.dll",
                    "Microsoft.Extensions.Logging.Console.dll",
                    "Microsoft.Extensions.Logging.Debug.dll",
                    "Microsoft.Extensions.Logging.dll",
                    "Microsoft.Extensions.Logging.EventLog.dll",
                    "Microsoft.Extensions.Logging.EventSource.dll",
                    "Microsoft.Extensions.Options.ConfigurationExtensions.dll",
                    "Microsoft.Extensions.Options.dll",
                    "Microsoft.Extensions.Primitives.dll",
                    "Microsoft.Xaml.Behaviors.dll",
                    "Redball.Core.dll",
                    "Redball.Core.pdb",
                    "SkiaSharp.dll",
                    "SkiaSharp.SceneGraph.dll",
                    "SkiaSharp.Skottie.dll",
                    "SkiaSharp.Views.Desktop.Common.dll",
                    "SkiaSharp.Views.WPF.dll",
                    "SQLitePCLRaw.batteries_v2.dll",
                    "SQLitePCLRaw.core.dll",
                    "SQLitePCLRaw.provider.e_sqlite3.dll",
                    "System.Diagnostics.EventLog.dll",
                    "System.Management.dll",
                    "System.ServiceProcess.ServiceController.dll",
                    "System.Speech.dll"
                };
                
                // Other misplaced files to remove from root
                var otherFilesToRemove = new[]
                {
                    "analytics.json",
                    "engine_toggle.json",
                    "pomodoro_timer.json",
                    "ram_usage.json",
                    "Redball.state.json",
                    "templates.json",
                    "typething_launch.json",
                    "InputInterceptor.dll"
                };
                
                int removedCount = 0;
                
                // Remove misplaced DLL files
                foreach (var fileName in dllFilesToRemove)
                {
                    var filePath = Path.Combine(installFolder, fileName);
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            File.Delete(filePath);
                            LogMessage(session, $"  Removed misplaced file: {fileName}");
                            removedCount++;
                        }
                        catch (Exception ex)
                        {
                            LogMessage(session, $"  Warning: Could not remove {fileName}: {ex.Message}");
                        }
                    }
                }
                
                // Remove other misplaced files
                foreach (var fileName in otherFilesToRemove)
                {
                    var filePath = Path.Combine(installFolder, fileName);
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            File.Delete(filePath);
                            LogMessage(session, $"  Removed misplaced file: {fileName}");
                            removedCount++;
                        }
                        catch (Exception ex)
                        {
                            LogMessage(session, $"  Warning: Could not remove {fileName}: {ex.Message}");
                        }
                    }
                }
                
                LogMessage(session, $"Cleanup complete. Removed {removedCount} misplaced file(s).");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                LogMessage(session, $"Warning during orphaned file cleanup: {ex.Message}");
                return ActionResult.Success;
            }
        }

        [CustomAction]
        public static ActionResult CleanupUserData(Session session)
        {
            try
            {
                LogMessage(session, "Starting full cleanup of user data...");
                
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var redballPath = Path.Combine(localAppData, "Redball");
                
                if (Directory.Exists(redballPath))
                {
                    try
                    {
                        Directory.Delete(redballPath, true);
                        LogMessage(session, $"Removed: {redballPath}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage(session, $"Warning: Could not remove {redballPath}: {ex.Message}");
                    }
                }
                
                LogMessage(session, "User data cleanup completed.");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                LogMessage(session, $"Warning during cleanup: {ex.Message}");
                return ActionResult.Success;
            }
        }

        [CustomAction]
        public static ActionResult FinalizeLogging(Session session)
        {
            try
            {
                LogMessage(session, "=== Installer Session Complete ===");
                
                var logPath = session["REDBALL_LOGPATH"];
                if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
                {
                    var installFolder = session["INSTALLFOLDER"];
                    if (!string.IsNullOrEmpty(installFolder))
                    {
                        var logsFolder = Path.Combine(installFolder, "logs");
                        try
                        {
                            Directory.CreateDirectory(logsFolder);
                            var destPath = Path.Combine(logsFolder, $"install_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                            File.Copy(logPath, destPath, true);
                            session.Log($"Installer log saved to: {destPath}");
                        }
                        catch (Exception ex) 
                        { 
                            session.Log($"Warning: Could not copy installer log: {ex.Message}");
                        }
                    }
                    
                    try { File.Delete(logPath); } 
                    catch (Exception ex) 
                    { 
                        session.Log($"Warning: Could not delete temp log: {ex.Message}");
                    }
                }
                
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"Warning in FinalizeLogging: {ex.Message}");
                return ActionResult.Success;
            }
        }

        private static void LogMessage(Session session, string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logLine = $"[{timestamp}] {message}";
            
            session.Log(logLine);
            
            try
            {
                if (!string.IsNullOrEmpty(_sessionLogPath))
                {
                    File.AppendAllText(_sessionLogPath, logLine + Environment.NewLine);
                }
            }
            catch (Exception ex) 
            { 
                session.Log($"Warning: Could not write to session log: {ex.Message}");
            }
        }

        private static void CopyDirectoryRecursive(DirectoryInfo source, string destination, Session session)
        {
            Directory.CreateDirectory(destination);
            
            foreach (var file in source.GetFiles())
            {
                try
                {
                    var destFile = Path.Combine(destination, file.Name);
                    file.CopyTo(destFile, true);
                    LogMessage(session, $"  Copied: {file.Name}");
                }
                catch (Exception ex)
                {
                    LogMessage(session, $"  Warning: Could not copy {file.Name}: {ex.Message}");
                }
            }
            
            foreach (var subDir in source.GetDirectories())
            {
                var newDest = Path.Combine(destination, subDir.Name);
                CopyDirectoryRecursive(subDir, newDest, session);
            }
        }
    }
}
