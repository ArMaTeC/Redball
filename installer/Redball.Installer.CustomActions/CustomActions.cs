using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using WixToolset.Dtf.WindowsInstaller;

namespace Redball.Installer
{
    public class CustomActions
    {
        private static readonly string LogFileName = "Redball_Install_Log.txt";
        private static string? _sessionLogPath;

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
                        catch { }
                    }
                    
                    try { File.Delete(logPath); } catch { }
                }
                
                return ActionResult.Success;
            }
            catch
            {
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
            catch { }
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
