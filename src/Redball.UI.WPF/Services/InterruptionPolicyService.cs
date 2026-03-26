using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Types of interruptions.
/// </summary>
public enum InterruptionType
{
    ModalDialog,
    ToastNotification,
    BannerMessage,
    SilentUpdate,
    RequestLater
}

/// <summary>
/// User activity state.
/// </summary>
public enum UserActivityState
{
    Idle,
    Active,
    Focused,
    Inactive
}

/// <summary>
/// Interruption request.
/// </summary>
public class InterruptionRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public InterruptionType RequestedType { get; set; }
    public InterruptionType? ApprovedType { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? DenialReason { get; set; }
    public bool CanDefer { get; set; } = true;
    public TimeSpan? AutoDismissDelay { get; set; }
    public Action? OnDismiss { get; set; }
}

/// <summary>
/// Service for managing interruption policy.
/// Implements ux-5 from improve_me.txt: Interruption policy - no blocking dialogs during active sessions.
/// </summary>
public class InterruptionPolicyService
{
    private static readonly Lazy<InterruptionPolicyService> _instance = new(() => new InterruptionPolicyService());
    public static InterruptionPolicyService Instance => _instance.Value;

    private UserActivityState _currentActivityState = UserActivityState.Idle;
    private readonly List<InterruptionRequest> _pendingRequests = new();
    private readonly object _lock = new();

    public event EventHandler<InterruptionRequest>? InterruptionRequested;
    public event EventHandler<InterruptionRequest>? InterruptionApproved;
    public event EventHandler<InterruptionRequest>? InterruptionDenied;

    private InterruptionPolicyService()
    {
        Logger.Info("InterruptionPolicyService", "Interruption policy service initialized");
    }

    /// <summary>
    /// Current user activity state.
    /// </summary>
    public UserActivityState CurrentActivityState
    {
        get
        {
            lock (_lock)
            {
                return _currentActivityState;
            }
        }
    }

    /// <summary>
    /// Updates the user activity state.
    /// </summary>
    public void UpdateActivityState(UserActivityState state)
    {
        var previousState = _currentActivityState;

        lock (_lock)
        {
            _currentActivityState = state;
        }

        if (previousState != state)
        {
            Logger.Info("InterruptionPolicyService", $"Activity state: {previousState} -> {state}");

            // Process pending requests if user became less active
            if (state == UserActivityState.Idle || state == UserActivityState.Inactive)
            {
                _ = ProcessPendingRequestsAsync();
            }
        }
    }

    /// <summary>
    /// Requests an interruption (dialog, notification, etc.).
    /// </summary>
    public InterruptionRequest RequestInterruption(
        string title,
        string message,
        InterruptionType preferredType = InterruptionType.ToastNotification,
        bool canDefer = true)
    {
        var request = new InterruptionRequest
        {
            Title = title,
            Message = message,
            RequestedType = preferredType,
            RequestedAt = DateTime.Now,
            CanDefer = canDefer
        };

        lock (_lock)
        {
            _pendingRequests.Add(request);
        }

        Logger.Info("InterruptionPolicyService", $"Interruption requested: {title} ({preferredType})");
        InterruptionRequested?.Invoke(this, request);

        // Try to process immediately
        _ = ProcessRequestAsync(request);

        return request;
    }

    /// <summary>
    /// Approves a pending interruption.
    /// </summary>
    public void ApproveInterruption(string requestId, InterruptionType approvedType)
    {
        lock (_lock)
        {
            var request = _pendingRequests.FirstOrDefault(r => r.Id == requestId);
            if (request == null) return;

            request.ApprovedType = approvedType;
            request.ApprovedAt = DateTime.Now;

            _pendingRequests.Remove(request);

            Logger.Info("InterruptionPolicyService", $"Interruption approved: {request.Title} as {approvedType}");
            InterruptionApproved?.Invoke(this, request);

            // Execute the approved interruption
            ExecuteInterruption(request);
        }
    }

    /// <summary>
    /// Denies a pending interruption.
    /// </summary>
    public void DenyInterruption(string requestId, string reason)
    {
        lock (_lock)
        {
            var request = _pendingRequests.FirstOrDefault(r => r.Id == requestId);
            if (request == null) return;

            request.DenialReason = reason;
            _pendingRequests.Remove(request);

            Logger.Info("InterruptionPolicyService", $"Interruption denied: {request.Title} - {reason}");
            InterruptionDenied?.Invoke(this, request);

            // Callback if provided
            request.OnDismiss?.Invoke();
        }
    }

    /// <summary>
    /// Gets pending interruption requests.
    /// </summary>
    public IReadOnlyList<InterruptionRequest> GetPendingRequests()
    {
        lock (_lock)
        {
            return _pendingRequests.ToList();
        }
    }

    private async Task ProcessRequestAsync(InterruptionRequest request)
    {
        var activityState = CurrentActivityState;
        var approvedType = DetermineInterruptionType(request.RequestedType, activityState);

        if (approvedType == request.RequestedType || !request.CanDefer)
        {
            // Approve immediately
            ApproveInterruption(request.Id, approvedType);
        }
        else
        {
            // Defer until appropriate
            Logger.Info("InterruptionPolicyService", $"Interruption deferred: {request.Title} until user less active");

            // Show subtle indicator if available
            ShowDeferredIndicator(request);
        }
    }

    private async Task ProcessPendingRequestsAsync()
    {
        InterruptionRequest[] pending;
        lock (_lock)
        {
            pending = _pendingRequests.ToArray();
        }

        foreach (var request in pending)
        {
            await ProcessRequestAsync(request);
        }
    }

    private InterruptionType DetermineInterruptionType(InterruptionType requested, UserActivityState state)
    {
        return state switch
        {
            UserActivityState.Focused => requested switch
            {
                InterruptionType.ModalDialog => InterruptionType.SilentUpdate,
                InterruptionType.ToastNotification => InterruptionType.BannerMessage,
                _ => requested
            },
            UserActivityState.Active => requested switch
            {
                InterruptionType.ModalDialog => InterruptionType.ToastNotification,
                _ => requested
            },
            _ => requested
        };
    }

    private void ExecuteInterruption(InterruptionRequest request)
    {
        var type = request.ApprovedType ?? request.RequestedType;

        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (type)
            {
                case InterruptionType.ModalDialog:
                    // Only show if absolutely necessary
                    MessageBox.Show(request.Message, request.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                    break;

                case InterruptionType.ToastNotification:
                    NotificationService.Instance.ShowInfo(request.Title, request.Message);
                    break;

                case InterruptionType.BannerMessage:
                    // Show non-intrusive banner
                    ShowBannerMessage(request.Title, request.Message);
                    break;

                case InterruptionType.SilentUpdate:
                    // Apply without notification
                    Logger.Info("InterruptionPolicyService", $"Silent update applied: {request.Title}");
                    break;

                case InterruptionType.RequestLater:
                    // Queue for later
                    Logger.Info("InterruptionPolicyService", $"Request queued for later: {request.Title}");
                    break;
            }

            request.OnDismiss?.Invoke();
        });
    }

    private void ShowBannerMessage(string title, string message)
    {
        // This would trigger UI to show a banner
        Logger.Info("InterruptionPolicyService", $"Banner shown: {title}");
    }

    private void ShowDeferredIndicator(InterruptionRequest request)
    {
        // Show subtle UI indicator that there's a pending message
        Logger.Debug("InterruptionPolicyService", $"Deferred indicator shown for: {request.Title}");
    }
}
