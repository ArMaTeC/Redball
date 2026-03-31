using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Policy scope (where policy is defined).
/// </summary>
public enum PolicyScope
{
    GroupPolicy,
    Registry,
    UserSettings,
    Default
}

/// <summary>
/// Enterprise policy entry.
/// </summary>
public class EnterprisePolicy
{
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public object? Value { get; set; }
    public PolicyScope Scope { get; set; }
    public bool IsEnforced { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Service for managing enterprise policies from Group Policy and registry.
/// Implements os-2 from improve_me.txt: Enterprise policy support.
/// </summary>
public class EnterprisePolicyService
{
    private static readonly Lazy<EnterprisePolicyService> _instance = new(() => new EnterprisePolicyService());
    public static EnterprisePolicyService Instance => _instance.Value;

    private readonly Dictionary<string, EnterprisePolicy> _policies = new();
    private readonly string _registryKey = @"Software\Policies\ArMaTeC\Redball";
    private readonly string _gpKey = @"Software\Policies\Microsoft\Windows\Redball";

    private EnterprisePolicyService()
    {
        LoadPolicies();
        Logger.Info("EnterprisePolicyService", "Enterprise policy service initialized");
    }

    /// <summary>
    /// Gets all active policies.
    /// </summary>
    public IReadOnlyDictionary<string, EnterprisePolicy> GetPolicies()
    {
        return _policies;
    }

    /// <summary>
    /// Gets a policy value with precedence (GP > Registry > User > Default).
    /// </summary>
    public T? GetPolicyValue<T>(string key, T? defaultValue = default)
    {
        // Check Group Policy first (highest precedence)
        var gpValue = GetGroupPolicyValue(key);
        if (gpValue != null)
        {
            Logger.Debug("EnterprisePolicyService", $"Policy {key} from Group Policy");
            return ConvertValue<T>(gpValue);
        }

        // Check registry policies
        var regValue = GetRegistryPolicyValue(key);
        if (regValue != null)
        {
            Logger.Debug("EnterprisePolicyService", $"Policy {key} from Registry");
            return ConvertValue<T>(regValue);
        }

        // Fall back to default
        return defaultValue;
    }

    /// <summary>
    /// Checks if a setting is managed by policy.
    /// </summary>
    public bool IsPolicyManaged(string key)
    {
        return _policies.ContainsKey(key) &&
               (_policies[key].Scope == PolicyScope.GroupPolicy ||
                _policies[key].Scope == PolicyScope.Registry);
    }

    /// <summary>
    /// Checks if a policy is enforced (locked).
    /// </summary>
    public bool IsPolicyEnforced(string key)
    {
        return _policies.TryGetValue(key, out var policy) && policy.IsEnforced;
    }

    /// <summary>
    /// Gets the scope of a policy.
    /// </summary>
    public PolicyScope GetPolicyScope(string key)
    {
        return _policies.TryGetValue(key, out var policy)
            ? policy.Scope
            : PolicyScope.Default;
    }

    /// <summary>
    /// Refreshes policies from registry.
    /// </summary>
    public void RefreshPolicies()
    {
        _policies.Clear();
        LoadPolicies();
        Logger.Info("EnterprisePolicyService", "Policies refreshed");
    }

    /// <summary>
    /// Gets a summary of policy configuration.
    /// </summary>
    public PolicySummary GetPolicySummary()
    {
        return new PolicySummary
        {
            TotalPolicies = _policies.Count,
            GroupPolicyCount = _policies.Count(p => p.Value.Scope == PolicyScope.GroupPolicy),
            RegistryPolicyCount = _policies.Count(p => p.Value.Scope == PolicyScope.Registry),
            EnforcedCount = _policies.Count(p => p.Value.IsEnforced),
            ManagedSettings = _policies.Where(p => p.Value.Scope != PolicyScope.Default)
                .Select(p => p.Key)
                .ToList()
        };
    }

    private void LoadPolicies()
    {
        // Load Group Policy settings
        LoadGroupPolicies();

        // Load Registry policies
        LoadRegistryPolicies();
    }

    private void LoadGroupPolicies()
    {
        try
        {
            // Check HKEY_LOCAL_MACHINE for machine policies
            using var lmKey = Registry.LocalMachine.OpenSubKey(_gpKey);
            if (lmKey != null)
            {
                foreach (var valueName in lmKey.GetValueNames())
                {
                    var value = lmKey.GetValue(valueName);
                    _policies[valueName] = new EnterprisePolicy
                    {
                        Name = valueName,
                        Key = valueName,
                        Value = value,
                        Scope = PolicyScope.GroupPolicy,
                        IsEnforced = true, // GP is always enforced
                        Description = $"Group Policy: {valueName}"
                    };
                }
            }

            // Check HKEY_CURRENT_USER for user policies
            using var cuKey = Registry.CurrentUser.OpenSubKey(_gpKey);
            if (cuKey != null)
            {
                foreach (var valueName in cuKey.GetValueNames())
                {
                    // Machine policy takes precedence
                    if (!_policies.ContainsKey(valueName))
                    {
                        var value = cuKey.GetValue(valueName);
                        _policies[valueName] = new EnterprisePolicy
                        {
                            Name = valueName,
                            Key = valueName,
                            Value = value,
                            Scope = PolicyScope.GroupPolicy,
                            IsEnforced = true,
                            Description = $"Group Policy (User): {valueName}"
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("EnterprisePolicyService", $"Failed to load Group Policies: {ex.Message}");
        }
    }

    private void LoadRegistryPolicies()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryKey);
            if (key == null) return;

            foreach (var valueName in key.GetValueNames())
            {
                // Skip if already set by Group Policy
                if (_policies.ContainsKey(valueName) &&
                    _policies[valueName].Scope == PolicyScope.GroupPolicy)
                {
                    continue;
                }

                var value = key.GetValue(valueName);
                _policies[valueName] = new EnterprisePolicy
                {
                    Name = valueName,
                    Key = valueName,
                    Value = value,
                    Scope = PolicyScope.Registry,
                    IsEnforced = false, // Registry policies can be overridden by GP
                    Description = $"Registry Policy: {valueName}"
                };
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("EnterprisePolicyService", $"Failed to load Registry Policies: {ex.Message}");
        }
    }

    private object? GetGroupPolicyValue(string key)
    {
        try
        {
            using var lmKey = Registry.LocalMachine.OpenSubKey(_gpKey);
            var value = lmKey?.GetValue(key);
            if (value != null) return value;

            using var cuKey = Registry.CurrentUser.OpenSubKey(_gpKey);
            return cuKey?.GetValue(key);
        }
        catch (Exception ex)
        {
            Logger.Debug("EnterprisePolicyService", $"Failed to get Group Policy value for {key}: {ex.Message}");
            return null;
        }
    }

    private object? GetRegistryPolicyValue(string key)
    {
        try
        {
            using var regKey = Registry.CurrentUser.OpenSubKey(_registryKey);
            return regKey?.GetValue(key);
        }
        catch (Exception ex)
        {
            Logger.Debug("EnterprisePolicyService", $"Failed to get Registry Policy value for {key}: {ex.Message}");
            return null;
        }
    }

    private T? ConvertValue<T>(object value)
    {
        try
        {
            if (value is T typedValue)
                return typedValue;

            return (T?)Convert.ChangeType(value, typeof(T));
        }
        catch (Exception ex)
        {
            Logger.Debug("EnterprisePolicyService", $"Failed to convert policy value to {typeof(T).Name}: {ex.Message}");
            return default;
        }
    }
}

/// <summary>
/// Policy configuration summary.
/// </summary>
public class PolicySummary
{
    public int TotalPolicies { get; set; }
    public int GroupPolicyCount { get; set; }
    public int RegistryPolicyCount { get; set; }
    public int EnforcedCount { get; set; }
    public List<string> ManagedSettings { get; set; } = new();

    public bool HasEnterprisePolicies => GroupPolicyCount > 0 || RegistryPolicyCount > 0;
}
