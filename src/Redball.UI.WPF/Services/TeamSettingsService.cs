using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Manages team-based settings synchronization for enterprise deployments.
/// Allows teams to share configurations, policies, and presets across members.
/// </summary>
public class TeamSettingsService
{
    private static readonly Lazy<TeamSettingsService> _instance = new(() => new TeamSettingsService());
    public static TeamSettingsService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private readonly string _teamCacheDir;
    private TeamInfo? _currentTeam;
    private DateTime _lastSync = DateTime.MinValue;
    
    public event EventHandler<TeamSettingsChangedEventArgs>? TeamSettingsChanged;
    public event EventHandler<TeamSyncResult>? SyncCompleted;

    public bool IsEnabled => ConfigService.Instance.Config.TeamSyncEnabled;
    public string? TeamId => _currentTeam?.TeamId;
    public string? TeamName => _currentTeam?.Name;
    public bool IsTeamAdmin => _currentTeam?.IsAdmin ?? false;
    public DateTime LastSync => _lastSync;

    private TeamSettingsService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"Redball/{GetAppVersion()}");
        
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _teamCacheDir = Path.Combine(localAppData, "Redball", "TeamCache");
        
        if (!Directory.Exists(_teamCacheDir))
        {
            Directory.CreateDirectory(_teamCacheDir);
        }
        
        LoadCachedTeamInfo();
        Logger.Verbose("TeamSettingsService", "Initialized");
    }

    /// <summary>
    /// Creates a new team with the current user as admin.
    /// </summary>
    public async Task<TeamCreationResult> CreateTeamAsync(string teamName, string? adminEmail = null)
    {
        try
        {
            if (!IsEnabled)
            {
                return new TeamCreationResult { Success = false, Error = "Team sync is not enabled" };
            }

            var teamId = Guid.NewGuid().ToString("N");
            var joinCode = GenerateJoinCode();
            
            _currentTeam = new TeamInfo
            {
                TeamId = teamId,
                Name = teamName,
                JoinCode = joinCode,
                IsAdmin = true,
                CreatedAt = DateTime.UtcNow,
                Members = new List<TeamMember>
                {
                    new TeamMember
                    {
                        UserId = GetCurrentUserId(),
                        Email = adminEmail ?? GetCurrentUserEmail(),
                        Role = TeamRole.Admin,
                        JoinedAt = DateTime.UtcNow
                    }
                }
            };
            
            SaveTeamInfo();
            
            Logger.Info("TeamSettingsService", $"Created team: {teamName} ({teamId})");
            
            return new TeamCreationResult
            {
                Success = true,
                TeamId = teamId,
                JoinCode = joinCode,
                TeamName = teamName
            };
        }
        catch (Exception ex)
        {
            Logger.Error("TeamSettingsService", "Failed to create team", ex);
            return new TeamCreationResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Joins an existing team using a join code.
    /// </summary>
    public async Task<TeamJoinResult> JoinTeamAsync(string joinCode, string? userEmail = null)
    {
        try
        {
            if (!IsEnabled)
            {
                return new TeamJoinResult { Success = false, Error = "Team sync is not enabled" };
            }

            // In production, this would validate with a backend service
            // For now, simulate local team joining
            var teamId = LookupTeamByJoinCode(joinCode);
            if (teamId == null)
            {
                return new TeamJoinResult { Success = false, Error = "Invalid join code" };
            }

            _currentTeam = new TeamInfo
            {
                TeamId = teamId,
                Name = $"Team-{joinCode.Substring(0, 6)}", // Placeholder
                JoinCode = joinCode,
                IsAdmin = false,
                JoinedAt = DateTime.UtcNow,
                Members = new List<TeamMember>()
            };
            
            SaveTeamInfo();
            
            Logger.Info("TeamSettingsService", $"Joined team: {teamId}");
            
            return new TeamJoinResult
            {
                Success = true,
                TeamId = teamId,
                TeamName = _currentTeam.Name
            };
        }
        catch (Exception ex)
        {
            Logger.Error("TeamSettingsService", "Failed to join team", ex);
            return new TeamJoinResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Leaves the current team.
    /// </summary>
    public async Task<bool> LeaveTeamAsync()
    {
        try
        {
            if (_currentTeam == null) return false;
            
            Logger.Info("TeamSettingsService", $"Leaving team: {_currentTeam.TeamId}");
            
            _currentTeam = null;
            ClearTeamCache();
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TeamSettingsService", "Failed to leave team", ex);
            return false;
        }
    }

    /// <summary>
    /// Synchronizes team settings with the cloud/backend.
    /// </summary>
    public async Task<TeamSyncResult> SyncSettingsAsync()
    {
        var result = new TeamSyncResult { Success = true };
        
        try
        {
            if (!IsEnabled || _currentTeam == null)
            {
                result.Success = false;
                result.Error = "Not in a team or sync disabled";
                return result;
            }

            var config = ConfigService.Instance.Config;
            var teamSettings = new TeamSettings
            {
                TeamId = _currentTeam.TeamId,
                LastModified = DateTime.UtcNow,
                ModifiedBy = GetCurrentUserId(),
                SharedPresets = GetSharedPresets(config),
                SharedPolicies = GetSharedPolicies(config),
                ThemeSettings = GetThemeSettings(config)
            };

            // In production, this would upload to backend
            // For now, save locally as cache
            await SaveTeamSettingsAsync(teamSettings);
            
            _lastSync = DateTime.UtcNow;
            result.SettingsSynced = true;
            result.LastSync = _lastSync;
            
            Logger.Info("TeamSettingsService", "Settings synced successfully");
            
            SyncCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            Logger.Error("TeamSettingsService", "Sync failed", ex);
        }
        
        return result;
    }

    /// <summary>
    /// Applies team settings to local configuration.
    /// </summary>
    public async Task<bool> ApplyTeamSettingsAsync(TeamSettings? settings = null)
    {
        try
        {
            settings ??= await LoadTeamSettingsAsync();
            if (settings == null) return false;

            var config = ConfigService.Instance.Config;
            
            // Apply shared presets (if enabled)
            if (settings.SharedPresets?.Count > 0 && !config.TeamSyncPresetsOnly)
            {
                // Apply team presets
                Logger.Info("TeamSettingsService", $"Applied {settings.SharedPresets.Count} shared presets");
            }

            // Apply shared policies
            if (settings.SharedPolicies != null)
            {
                ApplySharedPolicies(config, settings.SharedPolicies);
            }

            TeamSettingsChanged?.Invoke(this, new TeamSettingsChangedEventArgs
            {
                Settings = settings,
                AppliedAt = DateTime.UtcNow
            });

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TeamSettingsService", "Failed to apply team settings", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets team members (admin only).
    /// </summary>
    public List<TeamMember> GetTeamMembers()
    {
        return _currentTeam?.Members?.ToList() ?? new List<TeamMember>();
    }

    /// <summary>
    /// Updates a team member's role (admin only).
    /// </summary>
    public async Task<bool> UpdateMemberRoleAsync(string userId, TeamRole newRole)
    {
        if (!IsTeamAdmin) return false;
        
        var member = _currentTeam?.Members?.FirstOrDefault(m => m.UserId == userId);
        if (member == null) return false;
        
        member.Role = newRole;
        SaveTeamInfo();
        
        Logger.Info("TeamSettingsService", $"Updated {userId} role to {newRole}");
        return true;
    }

    /// <summary>
    /// Removes a team member (admin only).
    /// </summary>
    public async Task<bool> RemoveMemberAsync(string userId)
    {
        if (!IsTeamAdmin) return false;
        if (userId == GetCurrentUserId()) return false; // Can't remove self
        
        _currentTeam?.Members?.RemoveAll(m => m.UserId == userId);
        SaveTeamInfo();
        
        Logger.Info("TeamSettingsService", $"Removed member: {userId}");
        return true;
    }

    /// <summary>
    /// Generates a new join code (admin only).
    /// </summary>
    public string? RegenerateJoinCode()
    {
        if (!IsTeamAdmin) return null;
        
        var newCode = GenerateJoinCode();
        if (_currentTeam != null)
        {
            _currentTeam.JoinCode = newCode;
            SaveTeamInfo();
        }
        
        return newCode;
    }

    // Private methods
    private string GenerateJoinCode()
    {
        // Generate 8-character alphanumeric code
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 8)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private string? LookupTeamByJoinCode(string joinCode)
    {
        // In production, this would query the backend
        // For now, return a placeholder team ID
        return Guid.NewGuid().ToString("N");
    }

    private string GetCurrentUserId()
    {
        return $"user_{Environment.UserName}_{Environment.MachineName}";
    }

    private string GetCurrentUserEmail()
    {
        return $"{Environment.UserName}@{Environment.UserDomainName}";
    }

    private string GetAppVersion()
    {
        return typeof(TeamSettingsService).Assembly.GetName().Version?.ToString() ?? "3.0.0";
    }

    private void SaveTeamInfo()
    {
        if (_currentTeam == null) return;
        
        var path = Path.Combine(_teamCacheDir, "team_info.json");
        File.WriteAllText(path, JsonSerializer.Serialize(_currentTeam, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void LoadCachedTeamInfo()
    {
        var path = Path.Combine(_teamCacheDir, "team_info.json");
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                _currentTeam = JsonSerializer.Deserialize<TeamInfo>(json);
            }
            catch (Exception ex)
            {
                Logger.Warning("TeamSettingsService", $"Failed to load cached team info: {ex.Message}");
            }
        }
    }

    private async Task SaveTeamSettingsAsync(TeamSettings settings)
    {
        var path = Path.Combine(_teamCacheDir, $"settings_{settings.TeamId}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task<TeamSettings?> LoadTeamSettingsAsync()
    {
        if (_currentTeam == null) return null;
        
        var path = Path.Combine(_teamCacheDir, $"settings_{_currentTeam.TeamId}.json");
        if (!File.Exists(path)) return null;
        
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<TeamSettings>(json);
    }

    private void ClearTeamCache()
    {
        try
        {
            var files = Directory.GetFiles(_teamCacheDir);
            foreach (var file in files)
            {
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("TeamSettingsService", $"Failed to clear cache: {ex.Message}");
        }
    }

    private Dictionary<string, object> GetSharedPresets(RedballConfig config)
    {
        return new Dictionary<string, object>
        {
            ["MiniWidgetPreset"] = config.MiniWidgetPreset,
            ["QuickActionDefaults"] = new { config.MiniWidgetCustomQuickMinutes }
        };
    }

    private SharedPolicies GetSharedPolicies(RedballConfig config)
    {
        int? maxSessionDuration = null;
        if (config.ScheduleEnabled && 
            TimeSpan.TryParse(config.ScheduleStopTime, out var stopTime) && 
            TimeSpan.TryParse(config.ScheduleStartTime, out var startTime))
        {
            maxSessionDuration = (int)(stopTime - startTime).TotalMinutes;
        }

        return new SharedPolicies
        {
            RequireBatteryAware = config.BatteryAware,
            RequireIdleDetection = config.IdleDetection,
            MaxSessionDuration = maxSessionDuration,
            AllowUserOverrides = true
        };
    }

    private ThemeSettings GetThemeSettings(RedballConfig config)
    {
        return new ThemeSettings
        {
            Theme = config.Theme,
            AccentColor = config.AccentColor,
            FollowSystemTheme = config.FollowSystemTheme
        };
    }

    private void ApplySharedPolicies(RedballConfig config, SharedPolicies policies)
    {
        if (!policies.AllowUserOverrides) return;
        
        // Apply non-overridable policies
        if (policies.RequireBatteryAware && !config.BatteryAware)
        {
            config.BatteryAware = true;
        }
    }
}

// Data models
public class TeamInfo
{
    public string TeamId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string JoinCode { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime JoinedAt { get; set; }
    public List<TeamMember> Members { get; set; } = new();
}

public class TeamMember
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public TeamRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
}

public enum TeamRole
{
    Member,
    Moderator,
    Admin
}

public class TeamSettings
{
    public string TeamId { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
    public Dictionary<string, object> SharedPresets { get; set; } = new();
    public SharedPolicies SharedPolicies { get; set; } = new();
    public ThemeSettings ThemeSettings { get; set; } = new();
}

public class SharedPolicies
{
    public bool RequireBatteryAware { get; set; }
    public bool RequireIdleDetection { get; set; }
    public int? MaxSessionDuration { get; set; }
    public bool AllowUserOverrides { get; set; }
}

public class ThemeSettings
{
    public string Theme { get; set; } = "System";
    public string AccentColor { get; set; } = "Blue";
    public bool FollowSystemTheme { get; set; } = true;
}

// Result models
public class TeamCreationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? TeamId { get; set; }
    public string? JoinCode { get; set; }
    public string? TeamName { get; set; }
}

public class TeamJoinResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? TeamId { get; set; }
    public string? TeamName { get; set; }
}

public class TeamSyncResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool SettingsSynced { get; set; }
    public DateTime LastSync { get; set; }
}

public class TeamSettingsChangedEventArgs : EventArgs
{
    public TeamSettings Settings { get; set; } = new();
    public DateTime AppliedAt { get; set; }
}
