using Redball.Core.Security;
using Redball.Core.Sync;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Redball.UI.Services;

/// <summary>
/// Native AOT compatibility layer for PublishAOT builds.
/// Ensures JSON serialization, reflection, and dynamic code work with trimming.
/// </summary>
public static class AotCompatibility
{
    /// <summary>
    /// JSON serializer options pre-configured for AOT compatibility.
    /// Uses source generators instead of runtime reflection.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        TypeInfoResolver = new RedballJsonContext(),
    };

    /// <summary>
    /// Initializes AOT compatibility layer at startup.
    /// </summary>
    public static void Initialize()
    {
        // Verify source generators are working
        try
        {
            var testConfig = new RedballConfig();
            var json = JsonSerializer.Serialize(testConfig, JsonOptions);
            // SECURITY: Use SecureJsonSerializer with size limit and max depth
            var _ = SecureJsonSerializer.Deserialize<RedballConfig>(json);
            
            Debug.WriteLine("[AotCompatibility] AOT JSON serialization verified");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AotCompatibility] AOT JSON serialization failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets a property value using AOT-safe reflection.
    /// </summary>
    public static object? GetPropertyValue<T>(T instance, string propertyName)
    {
        // Use compiled delegates or source generators instead of reflection
        // This is a simplified version - production would use strongly-typed delegates
        
        var type = typeof(T);
        var cache = PropertyCache<T>.GetCache();
        
        if (cache.TryGetValue(propertyName, out var getter))
        {
            return getter?.Invoke(instance);
        }

        // Fallback to reflection (will be trimmed warning)
        var property = type.GetProperty(propertyName);
        return property?.GetValue(instance);
    }

    /// <summary>
    /// Sets a property value using AOT-safe reflection.
    /// </summary>
    public static void SetPropertyValue<T>(T instance, string propertyName, object value)
    {
        var type = typeof(T);
        var cache = PropertySetterCache<T>.GetCache();
        
        if (cache.TryGetValue(propertyName, out var setter))
        {
            setter?.Invoke(instance, value);
            return;
        }

        // Fallback
        var property = type.GetProperty(propertyName);
        property?.SetValue(instance, value);
    }

    /// <summary>
    /// Creates an instance using AOT-safe factory.
    /// </summary>
    public static T CreateInstance<T>() where T : new()
    {
        return new T();
    }

    /// <summary>
    /// Serializes to JSON with AOT compatibility.
    /// </summary>
    public static string SerializeJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// Deserializes from JSON with AOT compatibility.
    /// </summary>
    public static T? DeserializeJson<T>(string json)
    {
        // SECURITY: Validate size before parsing
        if (string.IsNullOrEmpty(json) || json.Length > 10 * 1024 * 1024) // 10MB limit
            return default;
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}

/// <summary>
/// JSON serialization context for source generators.
/// Marked with JsonSerializable attributes for all known types.
/// </summary>
[JsonSerializable(typeof(RedballConfig))]
[JsonSerializable(typeof(WidgetData))]
[JsonSerializable(typeof(DiagnosticsManifest))]
//[JsonSerializable(typeof(BuildProvenance))] // TODO: Add when class exists
[JsonSerializable(typeof(SlsaProvenance))]
[JsonSerializable(typeof(DeltaUpdateManifest))]
[JsonSerializable(typeof(DeltaPatch))]
[JsonSerializable(typeof(TokenSyncResult))]
[JsonSerializable(typeof(StartupReport))]
[JsonSerializable(typeof(MemoryPoolStats))]
[JsonSerializable(typeof(List<SettingsOperation>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
public partial class RedballJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Property getter cache for AOT-safe reflection.
/// </summary>
public static class PropertyCache<T>
{
    private static readonly Dictionary<string, Func<T, object?>> _cache = new();
    private static bool _initialized;

    public static Dictionary<string, Func<T, object?>> GetCache()
    {
        if (!_initialized)
        {
            InitializeCache();
            _initialized = true;
        }
        return _cache;
    }

    private static void InitializeCache()
    {
        // Source generator would populate this at compile time
        // For now, we use reflection to build the cache once
        var type = typeof(T);
        
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead))
        {
            try
            {
                var getter = CreateGetter(property);
                if (getter != null)
                {
                    _cache[property.Name] = getter;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AotCompatibility] Failed to compile getter for {property.Name}: {ex.Message}");
                // Continue with other properties - don't re-throw
            }
        }
    }

    private static Func<T, object?>? CreateGetter(System.Reflection.PropertyInfo property)
    {
        // Use expression trees or delegates instead of reflection for AOT
        // This is a simplified placeholder
        return (T instance) => property.GetValue(instance);
    }
}

/// <summary>
/// Property setter cache for AOT-safe reflection.
/// </summary>
public static class PropertySetterCache<T>
{
    private static readonly Dictionary<string, Action<T, object?>> _cache = new();
    private static bool _initialized;

    public static Dictionary<string, Action<T, object?>> GetCache()
    {
        if (!_initialized)
        {
            InitializeCache();
            _initialized = true;
        }
        return _cache;
    }

    private static void InitializeCache()
    {
        var type = typeof(T);
        
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite))
        {
            try
            {
                var setter = CreateSetter(property);
                if (setter != null)
                {
                    _cache[property.Name] = setter;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AotCompatibility] Failed to compile setter for {property.Name}: {ex.Message}");
                // Continue with other properties - don't re-throw
            }
        }
    }

    private static Action<T, object?>? CreateSetter(System.Reflection.PropertyInfo property)
    {
        return (T instance, object? value) => property.SetValue(instance, value);
    }
}

/// <summary>
/// Trimming-safe type resolver.
/// </summary>
public static class TypeResolver
{
    private static readonly Dictionary<string, Type> _knownTypes = new();

    static TypeResolver()
    {
        // Register all known types for trimming safety
        RegisterType<RedballConfig>();
        RegisterType<WidgetData>();
        //RegisterType<BuildProvenance>(); // TODO: Add when class exists
        RegisterType<SlsaProvenance>();
        RegisterType<DeltaUpdateManifest>();
        RegisterType<DeltaPatch>();
        RegisterType<DiagnosticsManifest>();
        RegisterType<StartupReport>();
        RegisterType<MemoryPoolStats>();
    }

    public static void RegisterType<T>()
    {
        var type = typeof(T);
        _knownTypes[type.FullName!] = type;
        _knownTypes[type.Name] = type;
    }

    public static Type? ResolveType(string name)
    {
        if (_knownTypes.TryGetValue(name, out var type))
        {
            return type;
        }

        // Fallback - will fail in trimmed builds
        return Type.GetType(name);
    }
}

/// <summary>
/// AOT-safe configuration for MSBuild integration.
/// </summary>
public class AotBuildConfiguration
{
    /// <summary>
    /// Gets the MSBuild properties for AOT publishing.
    /// </summary>
    public static Dictionary<string, string> GetPublishProperties()
    {
        return new Dictionary<string, string>
        {
            ["PublishAot"] = "true",
            ["TrimMode"] = "partial", // Start with partial trimming
            ["TrimmerSingleWarn"] = "false",
            ["TrimmerRemoveSymbols"] = "false",
            ["InvariantGlobalization"] = "false", // We need globalization for locales
            ["UseWindowsThreadPool"] = "true",
            ["IlcOptimizationPreference"] = "Speed",
            ["IlcInstructionSet"] = "x86-x64-v3",
            ["StackTraceSupport"] = "true", // Keep for debugging
            ["UseWindowsUIFonts"] = "true"
        };
    }

    /// <summary>
    /// Validates that the build is AOT-compatible.
    /// </summary>
    public static AotValidationResult ValidateBuild(string outputPath)
    {
        var result = new AotValidationResult { IsValid = true };
        var exePath = Path.Combine(outputPath, "Redball.UI.WPF.exe");

        if (!File.Exists(exePath))
        {
            result.IsValid = false;
            result.Errors.Add("Executable not found");
            return result;
        }

        var fileInfo = new FileInfo(exePath);
        result.FileSize = fileInfo.Length;

        // Check for AOT markers in the binary
        // Native AOT binaries have different characteristics
        using var fs = File.OpenRead(exePath);
        var header = new byte[2];
        fs.ReadExactly(header, 0, 2);

        // MZ header indicates traditional .NET, not AOT
        if (header[0] == 'M' && header[1] == 'Z')
        {
            result.Warnings.Add("Binary appears to be traditional .NET (not AOT)");
            result.IsAotBuild = false;
        }
        else
        {
            result.IsAotBuild = true;
        }

        return result;
    }
}

public class AotValidationResult
{
    public bool IsValid { get; set; }
    public bool IsAotBuild { get; set; }
    public long FileSize { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
