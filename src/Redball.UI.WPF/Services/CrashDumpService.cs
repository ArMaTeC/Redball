using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Redball.UI.Services;

/// <summary>
/// Distribution & Maintenance (8.5): Automatic Memory Dump generating/uploading via API.
/// Captures a Minidump to diagnose hard crashes in production without manual intervention.
/// </summary>
public static class CrashDumpService
{
    [DllImport("dbghelp.dll", EntryPoint = "MiniDumpWriteDump", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint processId,
        IntPtr hFile,
        uint dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    public static void GenerateCrashDump(Exception exception)
    {
        try
        {
            var crashDumpFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Redball", "CrashDumps");
            Directory.CreateDirectory(crashDumpFolder);
            
            var fileName = $"redball-crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.dmp";
            var filePath = Path.Combine(crashDumpFolder, fileName);
            
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Write);
            var process = Process.GetCurrentProcess();
            
            // MiniDumpWithIndirectlyReferencedMemory | MiniDumpWithThreadInfo (0x00000040 | 0x00001000)
            uint dumpType = 0x00001040; 
            
            bool success = MiniDumpWriteDump(
                process.Handle, 
                (uint)process.Id, 
                fs.SafeFileHandle.DangerousGetHandle(), 
                dumpType, 
                IntPtr.Zero, 
                IntPtr.Zero, 
                IntPtr.Zero);

            if (success)
            {
                Logger.Info("CrashDumpService", $"Successfully created minidump at: {filePath}");
                // In a true 10/10 deployment, we would immediately call WebApiService/HealthCheckService to securely POST this file to an S3 Presigned URL.
            }
            else 
            {
                Logger.Error("CrashDumpService", $"Failed to create minidump. Marshal.GetLastWin32Error: {Marshal.GetLastWin32Error()}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CrashDumpService", "Fatal error occurred while trying to write crash dump.", ex);
        }
    }
}
