namespace Redball.UI.Services;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Redball.Core.Input;

/// <summary>
/// Provides Windows Service-based keyboard input simulation.
/// This is an alternative to the HID driver approach that eliminates
/// the need for kernel driver installation and signing certificates.
/// Works over RDP sessions by using a helper process launched via
/// CreateProcessAsUser in the target session.
/// </summary>
public class ServiceInputProvider : IDisposable
{
    private static readonly Lazy<ServiceInputProvider> _instance = new(() => new ServiceInputProvider());
    public static ServiceInputProvider Instance => _instance.Value;

    private InputServiceClient? _client;
    private bool _initialized;
    private bool _disposed;
    private readonly object _lock = new();
    private string _lastErrorSummary = "None";
    private int _consecutiveFailures;

    /// <summary>
    /// Whether the service is installed and available.
    /// </summary>
    public bool IsServiceInstalled { get; private set; }

    /// <summary>
    /// Whether the provider is initialized and ready to send keystrokes.
    /// </summary>
    public bool IsReady => _initialized && _client != null;

    public bool IsInitialized => _initialized;
    public string LastErrorSummary => _lastErrorSummary;
    public int ConsecutiveFailures => _consecutiveFailures;

    [DllImport("user32.dll")]
    private static extern short VkKeyScanW(char ch);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern bool IsUserAnAdmin();

    private const uint MAPVK_VK_TO_VSC = 0;

    private ServiceInputProvider()
    {
        Logger.Verbose("ServiceInputProvider", "Instance created");
    }

    /// <summary>
    /// Checks if the current process is running with administrator privileges.
    /// </summary>
    private static bool IsRunningAsAdmin()
    {
        try
        {
            return IsUserAnAdmin();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the Redball Input Service is installed and running.
    /// Attempts to start the service if it's installed but stopped.
    /// </summary>
    public bool RefreshServiceInstalledState()
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("RedballInputService");
            var status = sc.Status;
            
            // Service is considered available only if it's running
            IsServiceInstalled = status == System.ServiceProcess.ServiceControllerStatus.Running;
            
            // If installed but not running, try to start it using sc.exe (requires admin rights)
            if (status == System.ServiceProcess.ServiceControllerStatus.Stopped)
            {
                // Check if running as administrator - required to start services
                if (!IsRunningAsAdmin())
                {
                    Logger.Info("ServiceInputProvider", "Service is installed but stopped. Administrator rights required to start the service automatically.");
                    _lastErrorSummary = "Service stopped - run as Administrator to auto-start";
                    IsServiceInstalled = false;
                    return IsServiceInstalled;
                }

                Logger.Info("ServiceInputProvider", "Service is installed but stopped, attempting to start via sc.exe (admin rights confirmed)...");
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = "start RedballInputService",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    
                    using var process = System.Diagnostics.Process.Start(psi)!;
                    var stdout = process.StandardOutput.ReadToEnd();
                    var stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    
                    if (process.ExitCode == 0 || stdout.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                    {
                        // Refresh status to confirm
                        sc.Refresh();
                        IsServiceInstalled = sc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
                        Logger.Info("ServiceInputProvider", $"Service started successfully: {IsServiceInstalled}");
                    }
                    else
                    {
                        Logger.Warning("ServiceInputProvider", $"sc.exe failed to start service: {stderr} {stdout}");
                        IsServiceInstalled = false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("ServiceInputProvider", $"Failed to start service via sc.exe: {ex.Message}");
                    IsServiceInstalled = false;
                }
            }
            else if (status == System.ServiceProcess.ServiceControllerStatus.StopPending || 
                     status == System.ServiceProcess.ServiceControllerStatus.StartPending)
            {
                Logger.Info("ServiceInputProvider", $"Service is in pending state ({status}), waiting...");
                sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                IsServiceInstalled = sc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("ServiceInputProvider", $"Failed to check service status: {ex.Message}");
            IsServiceInstalled = false;
        }
        return IsServiceInstalled;
    }

    /// <summary>
    /// Initializes the service connection.
    /// Call this once before sending keystrokes.
    /// </summary>
    public bool Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return IsReady;

            try
            {
                RefreshServiceInstalledState();

                if (!IsServiceInstalled)
                {
                    _lastErrorSummary = "Service not installed";
                    _consecutiveFailures++;
                    return false;
                }

                _client = new InputServiceClient();

                // Connect with short timeout - service should be running
                var connectTask = _client.ConnectAsync(2000);
                if (!connectTask.Wait(TimeSpan.FromSeconds(3)))
                {
                    _lastErrorSummary = "Connection timeout";
                    _consecutiveFailures++;
                    return false;
                }

                if (!connectTask.Result)
                {
                    _lastErrorSummary = "Failed to connect to service";
                    _consecutiveFailures++;
                    return false;
                }

                // Verify with ping
                var pingTask = _client.PingAsync();
                if (!pingTask.Wait(TimeSpan.FromSeconds(2)) || !pingTask.Result)
                {
                    _lastErrorSummary = "Service ping failed";
                    _consecutiveFailures++;
                    return false;
                }

                _initialized = true;
                _consecutiveFailures = 0;
                _lastErrorSummary = "None";
                Logger.Info("ServiceInputProvider", "Connected to Redball Input Service successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("ServiceInputProvider", "Initialization failed", ex);
                _lastErrorSummary = ex.Message;
                _consecutiveFailures++;
                return false;
            }
        }
    }

    /// <summary>
    /// Sends a single key press (down + up) through the service.
    /// </summary>
    public bool SendKeyPress(ushort keyCode, bool keyUp = false)
    {
        if (!IsReady) return false;

        try
        {
            var task = _client!.InjectKeyboardAsync(
                GetCurrentSessionId(),
                keyCode,
                keyUp,
                false);

            if (!task.Wait(TimeSpan.FromSeconds(5)))
            {
                _lastErrorSummary = "Key send timeout";
                return false;
            }

            return task.Result;
        }
        catch (Exception ex)
        {
            Logger.Debug("ServiceInputProvider", $"SendKeyPress failed: {ex.Message}");
            _lastErrorSummary = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Sends a character by mapping to virtual key and sending through service.
    /// </summary>
    public bool SendCharacter(char ch)
    {
        if (!IsReady) return false;

        try
        {
            // Handle special characters
            if (ch == '\t') return SendKeyPress(0x09); // VK_TAB
            if (ch == '\n' || ch == '\r') return SendKeyPress(0x0D); // VK_RETURN

            var vkResult = VkKeyScanW(ch);

            // Dead key or no mapping - fall back to local SendInput
            if (vkResult == -1 || (vkResult & 0x8000) != 0)
            {
                Logger.Debug("ServiceInputProvider", $"No VK mapping for '{ch}', falling back to local SendInput");
                return SendLocalUnicode(ch);
            }

            var vk = (byte)(vkResult & 0xFF);
            var shiftState = (byte)((vkResult >> 8) & 0xFF);

            var needShift = (shiftState & 1) != 0;
            var needCtrl = (shiftState & 2) != 0;
            var needAlt = (shiftState & 4) != 0;
            var needAltGr = needCtrl && needAlt;

            // Send modifiers
            if (needAltGr)
            {
                SendKeyPress(0x11, false); // Control down
                SendKeyPress(0x12, false); // Alt down
            }
            else
            {
                if (needShift) SendKeyPress(0x10, false); // Shift down
                if (needCtrl) SendKeyPress(0x11, false); // Control down
                if (needAlt) SendKeyPress(0x12, false); // Alt down
            }

            Thread.Sleep(2);

            // Send the key
            var result = SendKeyPress(vk);

            Thread.Sleep(2);

            // Release modifiers
            if (needAltGr)
            {
                SendKeyPress(0x12, true); // Alt up
                SendKeyPress(0x11, true); // Control up
            }
            else
            {
                if (needAlt) SendKeyPress(0x12, true);
                if (needCtrl) SendKeyPress(0x11, true);
                if (needShift) SendKeyPress(0x10, true);
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Debug("ServiceInputProvider", $"SendCharacter failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sends a character with retry logic for reliability.
    /// </summary>
    public bool SendCharacterWithRetry(char ch, int maxRetries = 3)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            if (SendCharacter(ch)) return true;
            Thread.Sleep(50 * (i + 1));
        }
        return false;
    }

    /// <summary>
    /// Sends a virtual key code (for special keys).
    /// </summary>
    public bool SendVirtualKey(ushort vk)
    {
        return SendKeyPress(vk);
    }

    /// <summary>
    /// Performs a health check and reconnects if needed.
    /// </summary>
    public void PerformHealthCheck()
    {
        if (!_initialized) return;

        try
        {
            var pingTask = _client?.PingAsync();
            if (pingTask == null || !pingTask.Wait(TimeSpan.FromSeconds(2)) || !pingTask.Result)
            {
                Logger.Warning("ServiceInputProvider", "Health check failed, reconnecting...");
                ReleaseResources("Health check failure");
                Initialize();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("ServiceInputProvider", $"Health check exception: {ex.Message}");
            ReleaseResources("Health check exception");
        }
    }

    /// <summary>
    /// Releases the service connection and cleans up.
    /// </summary>
    public void ReleaseResources(string reason = "Manual release")
    {
        lock (_lock)
        {
            Logger.Info("ServiceInputProvider", $"Releasing resources ({reason})...");

            try
            {
                _client?.Dispose();
                _client = null;
                _initialized = false;
            }
            catch (Exception ex)
            {
                Logger.Debug("ServiceInputProvider", $"ReleaseResources error: {ex.Message}");
            }

            Logger.Info("ServiceInputProvider", "Resources released");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseResources("Dispose");
    }

    /// <summary>
    /// Gets detailed service status including running state and pipe connectivity.
    /// </summary>
    public ServiceState GetDetailedServiceState()
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("RedballInputService");
            var status = sc.Status;
            
            var isRunning = status == System.ServiceProcess.ServiceControllerStatus.Running;
            var isInstalled = isRunning || 
                              status == System.ServiceProcess.ServiceControllerStatus.Stopped ||
                              status == System.ServiceProcess.ServiceControllerStatus.StartPending ||
                              status == System.ServiceProcess.ServiceControllerStatus.StopPending;
            
            if (!isInstalled)
            {
                return new ServiceState { Status = ServiceStatus.NotInstalled };
            }
            
            if (!isRunning)
            {
                return new ServiceState 
                { 
                    Status = ServiceStatus.Stopped,
                    ServiceControllerStatus = status.ToString()
                };
            }
            
            // Service is running - test pipe connectivity
            using var testClient = new InputServiceClient();
            var connected = testClient.ConnectAsync(3000).Result;
            
            if (!connected)
            {
                return new ServiceState 
                { 
                    Status = ServiceStatus.RunningNoPipe,
                    ServiceControllerStatus = "Running",
                    ErrorMessage = "Service running but named pipe connection failed"
                };
            }
            
            // Test ping
            var pingResult = testClient.PingAsync().Result;
            if (!pingResult)
            {
                return new ServiceState 
                { 
                    Status = ServiceStatus.RunningNoResponse,
                    ServiceControllerStatus = "Running",
                    PipeConnected = true,
                    ErrorMessage = "Connected but service not responding to ping"
                };
            }
            
            return new ServiceState 
            { 
                Status = ServiceStatus.Healthy,
                ServiceControllerStatus = "Running",
                PipeConnected = true,
                Responsive = true
            };
        }
        catch (Exception ex)
        {
            return new ServiceState 
            { 
                Status = ServiceStatus.Error,
                ErrorMessage = ex.Message
            };
        }
    }

    public record ServiceState
    {
        public ServiceStatus Status { get; init; }
        public string? ServiceControllerStatus { get; init; }
        public bool PipeConnected { get; init; }
        public bool Responsive { get; init; }
        public string? ErrorMessage { get; init; }
        
        public string GetDisplayText() => Status switch
        {
            ServiceStatus.NotInstalled => "Not installed",
            ServiceStatus.Stopped => $"Stopped ({ServiceControllerStatus})",
            ServiceStatus.RunningNoPipe => "Running (pipe error)",
            ServiceStatus.RunningNoResponse => "Running (no response)",
            ServiceStatus.Healthy => "Running (healthy)",
            ServiceStatus.Error => $"Error: {ErrorMessage}",
            _ => "Unknown"
        };
        
        public System.Windows.Media.Brush GetStatusBrush() => Status switch
        {
            ServiceStatus.NotInstalled => System.Windows.Media.Brushes.Gray,
            ServiceStatus.Stopped => System.Windows.Media.Brushes.Orange,
            ServiceStatus.RunningNoPipe => System.Windows.Media.Brushes.Gold,
            ServiceStatus.RunningNoResponse => System.Windows.Media.Brushes.Gold,
            ServiceStatus.Healthy => System.Windows.Media.Brushes.Green,
            ServiceStatus.Error => System.Windows.Media.Brushes.Red,
            _ => System.Windows.Media.Brushes.Gray
        };
    }

    public enum ServiceStatus
    {
        NotInstalled,
        Stopped,
        RunningNoPipe,
        RunningNoResponse,
        Healthy,
        Error
    }

    private static bool SendLocalUnicode(char ch)
    {
        // Local fallback using SendInput
        try
        {
            var inputs = new NativeINPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wScan = (ushort)ch;
            inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wScan = (ushort)ch;
            inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            return SendInput(2, inputs, Marshal.SizeOf<NativeINPUT>()) == 2;
        }
        catch (Exception ex)
        {
            Logger.Debug("ServiceInputProvider", $"SendLocalUnicode failed: {ex.Message}");
            return false;
        }
    }

    #region P/Invoke

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, NativeINPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeINPUT
    {
        public int type;
        public NativeINPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeINPUTUNION
    {
        [FieldOffset(0)] public NativeKEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static uint GetCurrentSessionId()
    {
        return (uint)Process.GetCurrentProcess().SessionId;
    }

    #endregion
}
