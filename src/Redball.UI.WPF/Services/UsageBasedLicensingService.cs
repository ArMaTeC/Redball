using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Usage-based licensing service for enterprise deployments.
/// Tracks feature usage and enforces license limits based on active users,
/// session hours, or feature utilization.
/// </summary>
public class UsageBasedLicensingService
{
    private static readonly Lazy<UsageBasedLicensingService> _instance = new(() => new UsageBasedLicensingService());
    public static UsageBasedLicensingService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private readonly string _licenseCachePath;
    private LicenseInfo? _currentLicense;
    private UsageTracker _usageTracker;

    public event EventHandler<LicenseStatusChangedEventArgs>? LicenseStatusChanged;
    public event EventHandler<UsageThresholdEventArgs>? UsageThresholdReached;
    public event EventHandler<LicenseViolationEventArgs>? LicenseViolationDetected;

    public bool IsLicensed => _currentLicense?.IsValid ?? false;
    public LicenseInfo? CurrentLicense => _currentLicense;
    public UsageStats CurrentUsage => _usageTracker.GetStats();

    private UsageBasedLicensingService()
    {
        _httpClient = new HttpClient();
        _usageTracker = new UsageTracker();
        
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _licenseCachePath = Path.Combine(appData, "Redball", "license.json");
        
        LoadCachedLicense();
        
        // Subscribe to usage events
        KeepAwakeService.Instance.ActiveStateChanged += OnKeepAwakeStateChanged;
        
        Logger.Verbose("UsageBasedLicensingService", "Initialized");
    }

    /// <summary>
    /// Activates a license with the provided license key.
    /// </summary>
    public async Task<LicenseActivationResult> ActivateLicenseAsync(string licenseKey)
    {
        try
        {
            var activation = new LicenseActivationRequest
            {
                LicenseKey = licenseKey,
                DeviceId = GetDeviceId(),
                DeviceName = Environment.MachineName,
                UserName = Environment.UserName,
                ActivatedAt = DateTime.UtcNow
            };

            // Validate with license server
            var validationResult = await ValidateLicenseWithServerAsync(activation);
            
            if (validationResult.IsValid)
            {
                _currentLicense = validationResult.LicenseInfo;
                SaveCachedLicense();

                LicenseStatusChanged?.Invoke(this, new LicenseStatusChangedEventArgs
                {
                    OldStatus = LicenseStatus.Unlicensed,
                    NewStatus = LicenseStatus.Active,
                    License = _currentLicense,
                    ChangedAt = DateTime.UtcNow
                });

                Logger.Info("UsageBasedLicensingService", 
                    $"License activated: {_currentLicense.LicenseType} ({_currentLicense.Tier})");

                return new LicenseActivationResult
                {
                    Success = true,
                    License = _currentLicense
                };
            }
            else
            {
                return new LicenseActivationResult
                {
                    Success = false,
                    Error = validationResult.ErrorMessage ?? "Invalid license key"
                };
            }
        }
        catch (Exception ex)
        {
            Logger.Error("UsageBasedLicensingService", "License activation failed", ex);
            return new LicenseActivationResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Checks current license status and reports usage.
    /// </summary>
    public async Task<LicenseStatus> CheckLicenseStatusAsync()
    {
        if (_currentLicense == null)
            return LicenseStatus.Unlicensed;

        // Check expiration
        if (_currentLicense.ExpiresAt.HasValue && _currentLicense.ExpiresAt.Value < DateTime.UtcNow)
        {
            _currentLicense.IsValid = false;
            
            LicenseStatusChanged?.Invoke(this, new LicenseStatusChangedEventArgs
            {
                OldStatus = LicenseStatus.Active,
                NewStatus = LicenseStatus.Expired,
                License = _currentLicense,
                ChangedAt = DateTime.UtcNow
            });

            return LicenseStatus.Expired;
        }

        // Report usage to server (for metered licenses)
        if (_currentLicense.BillingModel == BillingModel.UsageBased)
        {
            await ReportUsageAsync();
        }

        // Check usage limits
        var usage = _usageTracker.GetStats();
        var exceeded = CheckUsageLimits(usage);
        
        if (exceeded.Any())
        {
            LicenseViolationDetected?.Invoke(this, new LicenseViolationEventArgs
            {
                Violations = exceeded,
                License = _currentLicense,
                DetectedAt = DateTime.UtcNow
            });

            return LicenseStatus.LimitExceeded;
        }

        return LicenseStatus.Active;
    }

    /// <summary>
    /// Records feature usage for license tracking.
    /// </summary>
    public void RecordFeatureUsage(string featureName, int count = 1)
    {
        _usageTracker.RecordFeatureUsage(featureName, count);
        
        // Check if approaching limits
        CheckApproachingLimits();
    }

    /// <summary>
    /// Gets current usage statistics.
    /// </summary>
    public UsageStats GetUsageStats()
    {
        return _usageTracker.GetStats();
    }

    /// <summary>
    /// Deactivates the current license.
    /// </summary>
    public async Task<bool> DeactivateLicenseAsync()
    {
        if (_currentLicense == null)
            return false;

        try
        {
            // Notify server of deactivation
            await NotifyDeactivationAsync();

            _currentLicense.IsValid = false;
            SaveCachedLicense();

            LicenseStatusChanged?.Invoke(this, new LicenseStatusChangedEventArgs
            {
                OldStatus = LicenseStatus.Active,
                NewStatus = LicenseStatus.Unlicensed,
                License = null,
                ChangedAt = DateTime.UtcNow
            });

            Logger.Info("UsageBasedLicensingService", "License deactivated");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("UsageBasedLicensingService", "License deactivation failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Generates a usage report for billing purposes.
    /// </summary>
    public async Task<string> GenerateUsageReportAsync(DateTime startDate, DateTime endDate)
    {
        var report = new UsageBillingReport
        {
            LicenseId = _currentLicense?.LicenseId ?? "unknown",
            LicenseKey = _currentLicense?.LicenseKey ?? "unknown",
            Period = new BillingPeriod { Start = startDate, End = endDate },
            DeviceId = GetDeviceId(),
            GeneratedAt = DateTime.UtcNow,
            Usage = _usageTracker.GetStatsForPeriod(startDate, endDate),
            BillingModel = _currentLicense?.BillingModel ?? BillingModel.FlatRate,
            CalculatedAmount = CalculateBillableAmount(startDate, endDate)
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task<LicenseValidationResult> ValidateLicenseWithServerAsync(LicenseActivationRequest activation)
    {
        try
        {
            // In production, this would call the license server API
            // For now, simulate validation
            
            await Task.Delay(500); // Simulate network call
            
            // Simple validation: check key format
            if (string.IsNullOrEmpty(activation.LicenseKey) || activation.LicenseKey.Length < 16)
            {
                return new LicenseValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Invalid license key format"
                };
            }

            // Mock successful validation
            var licenseInfo = new LicenseInfo
            {
                LicenseId = Guid.NewGuid().ToString(),
                LicenseKey = activation.LicenseKey,
                LicenseType = DetermineLicenseType(activation.LicenseKey),
                Tier = DetermineTier(activation.LicenseKey),
                BillingModel = DetermineBillingModel(activation.LicenseKey),
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddYears(1),
                IsValid = true,
                Limits = GetLicenseLimits(activation.LicenseKey)
            };

            return new LicenseValidationResult
            {
                IsValid = true,
                LicenseInfo = licenseInfo
            };
        }
        catch (Exception ex)
        {
            return new LicenseValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Validation error: {ex.Message}"
            };
        }
    }

    private async Task ReportUsageAsync()
    {
        if (_currentLicense == null)
            return;

        try
        {
            var usage = _usageTracker.GetStats();
            
            var report = new UsageReport
            {
                LicenseId = _currentLicense.LicenseId,
                DeviceId = GetDeviceId(),
                ReportedAt = DateTime.UtcNow,
                Stats = usage
            };

            // Send to license server
            // await _httpClient.PostAsJsonAsync("...", report);
            
            Logger.Debug("UsageBasedLicensingService", "Usage reported to server");
        }
        catch (Exception ex)
        {
            Logger.Warning("UsageBasedLicensingService", $"Usage reporting failed: {ex.Message}");
        }
    }

    private async Task NotifyDeactivationAsync()
    {
        try
        {
            // Notify license server
            Logger.Debug("UsageBasedLicensingService", "Deactivation notification sent");
        }
        catch (Exception ex)
        {
            Logger.Warning("UsageBasedLicensingService", $"Deactivation notification failed: {ex.Message}");
        }
    }

    private void OnKeepAwakeStateChanged(object? sender, EventArgs e)
    {
        // Track session usage for license metering
        if (KeepAwakeService.Instance.IsActive)
        {
            _usageTracker.StartSession();
        }
        else
        {
            _usageTracker.EndSession();
        }
    }

    private void CheckApproachingLimits()
    {
        if (_currentLicense?.Limits == null)
            return;

        var usage = _usageTracker.GetStats();
        var limits = _currentLicense.Limits;

        // Check at 80% threshold
        if (limits.MaxMonthlyHours.HasValue)
        {
            var threshold = limits.MaxMonthlyHours.Value * 0.8;
            if (usage.MonthlyActiveHours >= threshold && usage.MonthlyActiveHours < limits.MaxMonthlyHours.Value)
            {
                UsageThresholdReached?.Invoke(this, new UsageThresholdEventArgs
                {
                    LimitType = UsageLimitType.MonthlyHours,
                    CurrentValue = usage.MonthlyActiveHours,
                    LimitValue = limits.MaxMonthlyHours.Value,
                    Threshold = 0.8,
                    IsExceeded = false,
                    ReachedAt = DateTime.UtcNow
                });
            }
        }
    }

    private List<UsageViolation> CheckUsageLimits(UsageStats usage)
    {
        var violations = new List<UsageViolation>();
        
        if (_currentLicense?.Limits == null)
            return violations;

        var limits = _currentLicense.Limits;

        if (limits.MaxMonthlyHours.HasValue && usage.MonthlyActiveHours > limits.MaxMonthlyHours.Value)
        {
            violations.Add(new UsageViolation
            {
                LimitType = UsageLimitType.MonthlyHours,
                CurrentValue = usage.MonthlyActiveHours,
                LimitValue = limits.MaxMonthlyHours.Value,
                ViolatedAt = DateTime.UtcNow
            });
        }

        if (limits.MaxConcurrentUsers.HasValue && usage.ConcurrentUsers > limits.MaxConcurrentUsers.Value)
        {
            violations.Add(new UsageViolation
            {
                LimitType = UsageLimitType.ConcurrentUsers,
                CurrentValue = usage.ConcurrentUsers,
                LimitValue = limits.MaxConcurrentUsers.Value,
                ViolatedAt = DateTime.UtcNow
            });
        }

        if (limits.MaxDevices.HasValue && usage.RegisteredDevices > limits.MaxDevices.Value)
        {
            violations.Add(new UsageViolation
            {
                LimitType = UsageLimitType.Devices,
                CurrentValue = usage.RegisteredDevices,
                LimitValue = limits.MaxDevices.Value,
                ViolatedAt = DateTime.UtcNow
            });
        }

        return violations;
    }

    private decimal CalculateBillableAmount(DateTime startDate, DateTime endDate)
    {
        var usage = _usageTracker.GetStatsForPeriod(startDate, endDate);
        
        return _currentLicense?.BillingModel switch
        {
            BillingModel.UsageBased => CalculateUsageBasedAmount(usage),
            BillingModel.PerUser => CalculatePerUserAmount(usage),
            BillingModel.PerDevice => CalculatePerDeviceAmount(usage),
            _ => _currentLicense?.BasePrice ?? 0
        };
    }

    private decimal CalculateUsageBasedAmount(UsageStats usage)
    {
        // $0.10 per hour of active time
        var hourlyRate = 0.10m;
        return (decimal)usage.TotalActiveHours * hourlyRate;
    }

    private decimal CalculatePerUserAmount(UsageStats usage)
    {
        // $5 per user per month
        var userRate = 5.00m;
        return usage.UniqueUsers * userRate;
    }

    private decimal CalculatePerDeviceAmount(UsageStats usage)
    {
        // $2 per device per month
        var deviceRate = 2.00m;
        return usage.RegisteredDevices * deviceRate;
    }

    private string GetDeviceId()
    {
        var machineName = Environment.MachineName;
        var userName = Environment.UserName;
        var combined = $"{machineName}:{userName}";
        
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash).Substring(0, 16).ToLower();
    }

    private void LoadCachedLicense()
    {
        if (File.Exists(_licenseCachePath))
        {
            try
            {
                var json = File.ReadAllText(_licenseCachePath);
                _currentLicense = JsonSerializer.Deserialize<LicenseInfo>(json);
                
                // Validate cached license
                if (_currentLicense?.ExpiresAt < DateTime.UtcNow)
                {
                    _currentLicense.IsValid = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("UsageBasedLicensingService", $"Failed to load cached license: {ex.Message}");
            }
        }
    }

    private void SaveCachedLicense()
    {
        if (_currentLicense == null)
            return;

        try
        {
            var json = JsonSerializer.Serialize(_currentLicense, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_licenseCachePath, json);
        }
        catch (Exception ex)
        {
            Logger.Warning("UsageBasedLicensingService", $"Failed to cache license: {ex.Message}");
        }
    }

    // Helper methods for determining license properties from key format
    private LicenseType DetermineLicenseType(string key) => key.StartsWith("ENT-") ? LicenseType.Enterprise : LicenseType.Standard;
    private LicenseTier DetermineTier(string key) => key.Contains("-PRO-") ? LicenseTier.Professional : LicenseTier.Basic;
    private BillingModel DetermineBillingModel(string key)
    {
        if (key.Contains("-USAGE-")) return BillingModel.UsageBased;
        if (key.Contains("-USER-")) return BillingModel.PerUser;
        if (key.Contains("-DEVICE-")) return BillingModel.PerDevice;
        return BillingModel.FlatRate;
    }
    
    private LicenseLimits GetLicenseLimits(string key)
    {
        var tier = DetermineTier(key);
        return tier switch
        {
            LicenseTier.Professional => new LicenseLimits 
            { 
                MaxMonthlyHours = 1000, 
                MaxConcurrentUsers = 50, 
                MaxDevices = 100 
            },
            _ => new LicenseLimits 
            { 
                MaxMonthlyHours = 100, 
                MaxConcurrentUsers = 5, 
                MaxDevices = 10 
            }
        };
    }
}

// License models
public class LicenseInfo
{
    public string LicenseId { get; set; } = string.Empty;
    public string LicenseKey { get; set; } = string.Empty;
    public LicenseType LicenseType { get; set; }
    public LicenseTier Tier { get; set; }
    public BillingModel BillingModel { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsValid { get; set; }
    public decimal BasePrice { get; set; }
    public LicenseLimits? Limits { get; set; }
}

public class LicenseLimits
{
    public int? MaxMonthlyHours { get; set; }
    public int? MaxConcurrentUsers { get; set; }
    public int? MaxDevices { get; set; }
    public int? MaxSessionsPerDay { get; set; }
}

public enum LicenseType
{
    Standard,
    Enterprise
}

public enum LicenseTier
{
    Basic,
    Professional,
    Enterprise
}

public enum BillingModel
{
    FlatRate,
    UsageBased,
    PerUser,
    PerDevice
}

public enum LicenseStatus
{
    Unlicensed,
    Active,
    Expired,
    LimitExceeded,
    Suspended
}

// Usage tracking
public class UsageTracker
{
    private DateTime _sessionStart;
    private bool _isTracking;
    private readonly List<UsageRecord> _records;

    public UsageTracker()
    {
        _records = new List<UsageRecord>();
    }

    public void StartSession()
    {
        _sessionStart = DateTime.UtcNow;
        _isTracking = true;
    }

    public void EndSession()
    {
        if (!_isTracking)
            return;

        var duration = DateTime.UtcNow - _sessionStart;
        _records.Add(new UsageRecord
        {
            StartTime = _sessionStart,
            Duration = duration,
            Type = UsageType.ActiveSession
        });

        _isTracking = false;
    }

    public void RecordFeatureUsage(string featureName, int count)
    {
        _records.Add(new UsageRecord
        {
            StartTime = DateTime.UtcNow,
            FeatureName = featureName,
            Count = count,
            Type = UsageType.FeatureUsage
        });
    }

    public UsageStats GetStats()
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);

        return new UsageStats
        {
            TotalActiveHours = _records.Where(r => r.Type == UsageType.ActiveSession).Sum(r => r.Duration.TotalHours),
            MonthlyActiveHours = _records.Where(r => r.Type == UsageType.ActiveSession && r.StartTime >= monthStart).Sum(r => r.Duration.TotalHours),
            TotalSessions = _records.Count(r => r.Type == UsageType.ActiveSession),
            FeatureUsage = _records.Where(r => r.Type == UsageType.FeatureUsage)
                .GroupBy(r => r.FeatureName)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Count)),
            ConcurrentUsers = 1, // Simplified
            RegisteredDevices = 1 // Simplified
        };
    }

    public UsageStats GetStatsForPeriod(DateTime start, DateTime end)
    {
        var periodRecords = _records.Where(r => r.StartTime >= start && r.StartTime <= end);

        return new UsageStats
        {
            TotalActiveHours = periodRecords.Where(r => r.Type == UsageType.ActiveSession).Sum(r => r.Duration.TotalHours),
            TotalSessions = periodRecords.Count(r => r.Type == UsageType.ActiveSession),
            FeatureUsage = periodRecords.Where(r => r.Type == UsageType.FeatureUsage)
                .GroupBy(r => r.FeatureName)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Count))
        };
    }
}

public class UsageRecord
{
    public DateTime StartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public UsageType Type { get; set; }
    public string? FeatureName { get; set; }
    public int Count { get; set; }
}

public enum UsageType
{
    ActiveSession,
    FeatureUsage
}

public class UsageStats
{
    public double TotalActiveHours { get; set; }
    public double MonthlyActiveHours { get; set; }
    public int TotalSessions { get; set; }
    public Dictionary<string, int> FeatureUsage { get; set; } = new();
    public int ConcurrentUsers { get; set; }
    public int RegisteredDevices { get; set; }
    public int UniqueUsers { get; set; }
}

// Request/Response models
public class LicenseActivationRequest
{
    public string LicenseKey { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
}

public class LicenseValidationResult
{
    public bool IsValid { get; set; }
    public LicenseInfo? LicenseInfo { get; set; }
    public string? ErrorMessage { get; set; }
}

public class LicenseActivationResult
{
    public bool Success { get; set; }
    public LicenseInfo? License { get; set; }
    public string? Error { get; set; }
}

public class LicenseUsageReport
{
    public string LicenseId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public DateTime ReportedAt { get; set; }
    public UsageStats Stats { get; set; } = new();
}

public class UsageBillingReport
{
    public string LicenseId { get; set; } = string.Empty;
    public string LicenseKey { get; set; } = string.Empty;
    public BillingPeriod Period { get; set; } = new();
    public string DeviceId { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public UsageStats Usage { get; set; } = new();
    public BillingModel BillingModel { get; set; }
    public decimal CalculatedAmount { get; set; }
}

public class BillingPeriod
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}

// Violations and limits
public class UsageViolation
{
    public UsageLimitType LimitType { get; set; }
    public double CurrentValue { get; set; }
    public double LimitValue { get; set; }
    public DateTime ViolatedAt { get; set; }
}

public enum UsageLimitType
{
    MonthlyHours,
    ConcurrentUsers,
    Devices,
    SessionsPerDay
}

// Event args
public class LicenseStatusChangedEventArgs : EventArgs
{
    public LicenseStatus OldStatus { get; set; }
    public LicenseStatus NewStatus { get; set; }
    public LicenseInfo? License { get; set; }
    public DateTime ChangedAt { get; set; }
}

public class UsageThresholdEventArgs : EventArgs
{
    public UsageLimitType LimitType { get; set; }
    public double CurrentValue { get; set; }
    public double LimitValue { get; set; }
    public double Threshold { get; set; }
    public bool IsExceeded { get; set; }
    public DateTime ReachedAt { get; set; }
}

public class LicenseViolationEventArgs : EventArgs
{
    public List<UsageViolation> Violations { get; set; } = new();
    public LicenseInfo? License { get; set; }
    public DateTime DetectedAt { get; set; }
}
