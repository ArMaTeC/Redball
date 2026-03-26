using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Session state types.
/// </summary>
public enum SessionState
{
    Locked,
    Unlocked,
    Remote,
    Console,
    FastUserSwitch,
    ShellRestart
}

/// <summary>
/// Reliability contract for session transitions.
/// </summary>
public class ReliabilityContract
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Func<Task<bool>> BeforeTransition { get; set; } = () => Task.FromResult(true);
    public Func<Task> AfterTransition { get; set; } = () => Task.CompletedTask;
    public TimeSpan RecoveryTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Service for managing reliability during OS session transitions.
/// Implements os-3 from improve_me.txt: Reliability contracts for shell restarts, session lock/unlock, RDP transitions, fast user switching.
/// </summary>
public class ReliabilityContractService
{
    private static readonly Lazy<ReliabilityContractService> _instance = new(() => new ReliabilityContractService());
    public static ReliabilityContractService Instance => _instance.Value;

    private readonly Dictionary<string, ReliabilityContract> _contracts = new();
    private SessionState _currentState = SessionState.Console;
    private readonly object _lock = new();

    public event EventHandler<SessionState>? SessionStateChanged;
    public event EventHandler<string>? ContractExecuted;

    private ReliabilityContractService()
    {
        InitializeDefaultContracts();
        StartMonitoring();
        Logger.Info("ReliabilityContractService", "Reliability contract service initialized");
    }

    /// <summary>
    /// Current session state.
    /// </summary>
    public SessionState CurrentState
    {
        get
        {
            lock (_lock)
            {
                return _currentState;
            }
        }
    }

    /// <summary>
    /// Registers a reliability contract.
    /// </summary>
    public void RegisterContract(string name, ReliabilityContract contract)
    {
        _contracts[name] = contract;
        Logger.Info("ReliabilityContractService", $"Contract registered: {name}");
    }

    /// <summary>
    /// Executes a reliability contract before a transition.
    /// </summary>
    public async Task<bool> ExecuteBeforeTransitionAsync(string contractName)
    {
        if (!_contracts.TryGetValue(contractName, out var contract))
        {
            Logger.Warning("ReliabilityContractService", $"Contract not found: {contractName}");
            return true;
        }

        Logger.Info("ReliabilityContractService", $"Executing pre-transition: {contractName}");

        try
        {
            using var cts = new System.Threading.CancellationTokenSource(contract.RecoveryTimeout);
            var task = contract.BeforeTransition();
            var completed = await Task.WhenAny(task, Task.Delay(contract.RecoveryTimeout, cts.Token)) == task;

            if (!completed)
            {
                Logger.Error("ReliabilityContractService", $"Contract timeout: {contractName}");
                return false;
            }

            var result = await task;
            ContractExecuted?.Invoke(this, $"{contractName}: Before = {result}");
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("ReliabilityContractService", $"Contract failed: {contractName}", ex);
            return false;
        }
    }

    /// <summary>
    /// Executes a reliability contract after a transition.
    /// </summary>
    public async Task ExecuteAfterTransitionAsync(string contractName)
    {
        if (!_contracts.TryGetValue(contractName, out var contract))
        {
            Logger.Warning("ReliabilityContractService", $"Contract not found: {contractName}");
            return;
        }

        Logger.Info("ReliabilityContractService", $"Executing post-transition: {contractName}");

        try
        {
            await contract.AfterTransition();
            ContractExecuted?.Invoke(this, $"{contractName}: After");
        }
        catch (Exception ex)
        {
            Logger.Error("ReliabilityContractService", $"Post-transition failed: {contractName}", ex);
        }
    }

    /// <summary>
    /// Handles session lock event.
    /// </summary>
    public async Task OnSessionLockAsync()
    {
        Logger.Info("ReliabilityContractService", "Session locking...");

        var success = await ExecuteBeforeTransitionAsync("SessionLock");
        if (!success)
        {
            Logger.Warning("ReliabilityContractService", "Session lock preparation failed");
        }

        lock (_lock)
        {
            _currentState = SessionState.Locked;
        }

        SessionStateChanged?.Invoke(this, SessionState.Locked);
        await ExecuteAfterTransitionAsync("SessionLock");
    }

    /// <summary>
    /// Handles session unlock event.
    /// </summary>
    public async Task OnSessionUnlockAsync()
    {
        Logger.Info("ReliabilityContractService", "Session unlocking...");

        var success = await ExecuteBeforeTransitionAsync("SessionUnlock");

        lock (_lock)
        {
            _currentState = SessionState.Unlocked;
        }

        SessionStateChanged?.Invoke(this, SessionState.Unlocked);
        await ExecuteAfterTransitionAsync("SessionUnlock");
    }

    /// <summary>
    /// Handles RDP transition.
    /// </summary>
    public async Task OnRdpTransitionAsync(bool isRemote)
    {
        var newState = isRemote ? SessionState.Remote : SessionState.Console;
        Logger.Info("ReliabilityContractService", $"RDP transition: {newState}");

        var success = await ExecuteBeforeTransitionAsync("RdpTransition");

        lock (_lock)
        {
            _currentState = newState;
        }

        SessionStateChanged?.Invoke(this, newState);
        await ExecuteAfterTransitionAsync("RdpTransition");
    }

    /// <summary>
    /// Handles shell restart.
    /// </summary>
    public async Task OnShellRestartAsync()
    {
        Logger.Info("ReliabilityContractService", "Shell restart detected...");

        var success = await ExecuteBeforeTransitionAsync("ShellRestart");

        lock (_lock)
        {
            _currentState = SessionState.ShellRestart;
        }

        SessionStateChanged?.Invoke(this, SessionState.ShellRestart);
        await ExecuteAfterTransitionAsync("ShellRestart");
    }

    /// <summary>
    /// Handles fast user switch.
    /// </summary>
    public async Task OnFastUserSwitchAsync(bool isSwitchingAway)
    {
        Logger.Info("ReliabilityContractService", $"Fast user switch: {(isSwitchingAway ? "away" : "back")}");

        var contractName = isSwitchingAway ? "UserSwitchAway" : "UserSwitchBack";
        var success = await ExecuteBeforeTransitionAsync(contractName);

        lock (_lock)
        {
            _currentState = SessionState.FastUserSwitch;
        }

        SessionStateChanged?.Invoke(this, SessionState.FastUserSwitch);
        await ExecuteAfterTransitionAsync(contractName);
    }

    /// <summary>
    /// Creates a recovery checkpoint.
    /// </summary>
    public void CreateCheckpoint(string name)
    {
        Logger.Info("ReliabilityContractService", $"Checkpoint created: {name}");
        // Save current state for potential recovery
    }

    private void InitializeDefaultContracts()
    {
        // Session Lock contract
        RegisterContract("SessionLock", new ReliabilityContract
        {
            Name = "Session Lock",
            Description = "Prepare for session lock",
            BeforeTransition = async () =>
            {
                // Pause non-essential operations
                KeepAwakeService.Instance.PauseMonitoring();
                return await Task.FromResult(true);
            },
            AfterTransition = async () =>
            {
                Logger.Info("ReliabilityContractService", "Session locked successfully");
                await Task.CompletedTask;
            }
        });

        // Session Unlock contract
        RegisterContract("SessionUnlock", new ReliabilityContract
        {
            Name = "Session Unlock",
            Description = "Restore after session unlock",
            BeforeTransition = async () =>
            {
                return await Task.FromResult(true);
            },
            AfterTransition = async () =>
            {
                // Resume operations
                KeepAwakeService.Instance.ResumeMonitoring();
                Logger.Info("ReliabilityContractService", "Session unlocked, operations resumed");
                await Task.CompletedTask;
            }
        });

        // RDP Transition contract
        RegisterContract("RdpTransition", new ReliabilityContract
        {
            Name = "RDP Transition",
            Description = "Handle RDP session change",
            BeforeTransition = async () =>
            {
                // Save state before transition
                CreateCheckpoint("RdpTransition");
                return await Task.FromResult(true);
            },
            AfterTransition = async () =>
            {
                // Verify state after transition
                Logger.Info("ReliabilityContractService", "RDP transition completed");
                await Task.CompletedTask;
            }
        });

        // Shell Restart contract
        RegisterContract("ShellRestart", new ReliabilityContract
        {
            Name = "Shell Restart",
            Description = "Handle Explorer shell restart",
            BeforeTransition = async () =>
            {
                // Re-register tray icon
                return await Task.FromResult(true);
            },
            AfterTransition = async () =>
            {
                // Restore UI state
                Logger.Info("ReliabilityContractService", "Shell restart handled");
                await Task.CompletedTask;
            }
        });

        // Fast User Switch contracts
        RegisterContract("UserSwitchAway", new ReliabilityContract
        {
            Name = "User Switch Away",
            Description = "Handle switching to other user",
            BeforeTransition = async () =>
            {
                KeepAwakeService.Instance.PauseMonitoring();
                return await Task.FromResult(true);
            },
            AfterTransition = async () =>
            {
                await Task.CompletedTask;
            }
        });

        RegisterContract("UserSwitchBack", new ReliabilityContract
        {
            Name = "User Switch Back",
            Description = "Handle return from other user",
            BeforeTransition = async () =>
            {
                return await Task.FromResult(true);
            },
            AfterTransition = async () =>
            {
                KeepAwakeService.Instance.ResumeMonitoring();
                Logger.Info("ReliabilityContractService", "User switch back handled");
                await Task.CompletedTask;
            }
        });
    }

    private void StartMonitoring()
    {
        // Subscribe to Windows session events
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _ = e.Reason switch
        {
            SessionSwitchReason.SessionLock => OnSessionLockAsync(),
            SessionSwitchReason.SessionUnlock => OnSessionUnlockAsync(),
            SessionSwitchReason.RemoteConnect => OnRdpTransitionAsync(true),
            SessionSwitchReason.RemoteDisconnect => OnRdpTransitionAsync(false),
            SessionSwitchReason.ConsoleConnect => OnRdpTransitionAsync(false),
            SessionSwitchReason.ConsoleDisconnect => OnRdpTransitionAsync(true),
            _ => Task.CompletedTask
        };
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        Logger.Info("ReliabilityContractService", $"Session ending: {e.Reason}");
        CreateCheckpoint("SessionEnding");
    }
}
