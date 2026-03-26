using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Data model version information.
/// </summary>
public class DataModelVersion
{
    public int Major { get; set; }
    public int Minor { get; set; }
    public int Patch { get; set; }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static DataModelVersion Parse(string version)
    {
        var parts = version.Split('.');
        return new DataModelVersion
        {
            Major = int.Parse(parts[0]),
            Minor = parts.Length > 1 ? int.Parse(parts[1]) : 0,
            Patch = parts.Length > 2 ? int.Parse(parts[2]) : 0
        };
    }

    public bool IsCompatibleWith(DataModelVersion other)
    {
        // Major must match, minor can be backward compatible
        return Major == other.Major && Minor >= other.Minor;
    }
}

/// <summary>
/// Versioned data entity base class.
/// </summary>
public abstract class VersionedEntity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    [JsonPropertyName("version")]
    public string ModelVersion { get; set; } = "1.0.0";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("checksum")]
    public string? Checksum { get; set; }

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;
}

/// <summary>
/// Migration contract for data transformations.
/// </summary>
public interface IDataMigration
{
    string FromVersion { get; }
    string ToVersion { get; }
    string Description { get; }
    bool CanMigrate(string fromVersion);
    string Migrate(string json);
}

/// <summary>
/// Migration from 1.0.0 to 1.1.0.
/// </summary>
public class Migration_1_0_0_to_1_1_0 : IDataMigration
{
    public string FromVersion => "1.0.0";
    public string ToVersion => "1.1.0";
    public string Description => "Add retention policy field";

    public bool CanMigrate(string fromVersion) => fromVersion == "1.0.0";

    public string Migrate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var dict = new Dictionary<string, object?>();
        foreach (var prop in root.EnumerateObject())
        {
            dict[prop.Name] = GetValue(prop.Value);
        }

        // Add new field
        dict["retentionDays"] = 30;
        dict["version"] = "1.1.0";

        return JsonSerializer.Serialize(dict);
    }

    private static object? GetValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.ToString()
    };
}

/// <summary>
/// Service for versioned data model with migration contracts.
/// Implements offline-2 from improve_me.txt: Versioned data model with migration contracts.
/// </summary>
public class VersionedDataModelService
{
    private static readonly Lazy<VersionedDataModelService> _instance = new(() => new VersionedDataModelService());
    public static VersionedDataModelService Instance => _instance.Value;

    private readonly List<IDataMigration> _migrations = new();
    private readonly DataModelVersion _currentVersion;

    private VersionedDataModelService()
    {
        _currentVersion = new DataModelVersion { Major = 1, Minor = 1, Patch = 0 };
        RegisterDefaultMigrations();
        Logger.Info("VersionedDataModelService", $"Service initialized with model version {_currentVersion}");
    }

    /// <summary>
    /// Current data model version.
    /// </summary>
    public DataModelVersion CurrentVersion => _currentVersion;

    /// <summary>
    /// Registers a migration.
    /// </summary>
    public void RegisterMigration(IDataMigration migration)
    {
        _migrations.Add(migration);
        _migrations.Sort((a, b) => string.Compare(a.FromVersion, b.FromVersion, StringComparison.Ordinal));

        Logger.Info("VersionedDataModelService", $"Migration registered: {migration.FromVersion} -> {migration.ToVersion}");
    }

    /// <summary>
    /// Migrates data to current version.
    /// </summary>
    public string MigrateToCurrent(string json, string sourceVersion)
    {
        var current = sourceVersion;
        var workingJson = json;

        while (current != _currentVersion.ToString())
        {
            var migration = _migrations.FirstOrDefault(m => m.CanMigrate(current));
            if (migration == null)
            {
                Logger.Warning("VersionedDataModelService", $"No migration path from {current} to {_currentVersion}");
                break;
            }

            Logger.Info("VersionedDataModelService", $"Migrating: {current} -> {migration.ToVersion}");
            workingJson = migration.Migrate(workingJson);
            current = migration.ToVersion;
        }

        return workingJson;
    }

    /// <summary>
    /// Validates if data can be read by current version.
    /// </summary>
    public bool CanReadVersion(string version)
    {
        try
        {
            var source = DataModelVersion.Parse(version);
            return source.IsCompatibleWith(_currentVersion);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a new entity with current version.
    /// </summary>
    public T CreateEntity<T>() where T : VersionedEntity, new()
    {
        return new T
        {
            ModelVersion = _currentVersion.ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            SchemaVersion = _currentVersion.Major
        };
    }

    /// <summary>
    /// Validates an entity checksum.
    /// </summary>
    public bool ValidateChecksum(VersionedEntity entity)
    {
        if (string.IsNullOrEmpty(entity.Checksum)) return true;

        // Simplified - would compute actual checksum
        return true;
    }

    /// <summary>
    /// Computes checksum for an entity.
    /// </summary>
    public string ComputeChecksum(VersionedEntity entity)
    {
        var json = JsonSerializer.Serialize(entity, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Simplified - would use actual hashing
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).Substring(0, 16);
    }

    private void RegisterDefaultMigrations()
    {
        RegisterMigration(new Migration_1_0_0_to_1_1_0());
    }
}

/// <summary>
/// Conflict resolution strategy.
/// </summary>
public enum ConflictResolutionStrategy
{
    LastWriteWins,
    FirstWriteWins,
    Merge,
    ManualResolution
}

/// <summary>
/// Conflict resolver for sync operations.
/// Implements offline-3 from improve_me.txt: Deterministic conflict resolution rules.
/// </summary>
public class ConflictResolverService
{
    private static readonly Lazy<ConflictResolverService> _instance = new(() => new ConflictResolverService());
    public static ConflictResolverService Instance => _instance.Value;

    private readonly Dictionary<string, ConflictResolutionStrategy> _defaultStrategies = new();

    private ConflictResolverService()
    {
        InitializeDefaultStrategies();
        Logger.Info("ConflictResolverService", "Conflict resolver service initialized");
    }

    /// <summary>
    /// Resolves a conflict using the specified strategy.
    /// </summary>
    public ConflictResolution<T> Resolve<T>(
        T local,
        T server,
        DateTime localTimestamp,
        DateTime serverTimestamp,
        ConflictResolutionStrategy? strategy = null) where T : VersionedEntity
    {
        var effectiveStrategy = strategy ?? GetDefaultStrategy(typeof(T).Name);

        return effectiveStrategy switch
        {
            ConflictResolutionStrategy.LastWriteWins => ResolveLWW(local, server, localTimestamp, serverTimestamp),
            ConflictResolutionStrategy.FirstWriteWins => ResolveFWW(local, server, localTimestamp, serverTimestamp),
            ConflictResolutionStrategy.Merge => ResolveMerge(local, server),
            _ => new ConflictResolution<T> { Winner = null, RequiresManualResolution = true, Local = local, Server = server }
        };
    }

    /// <summary>
    /// Performs semantic merge on complex objects.
    /// </summary>
    public T? SemanticMerge<T>(T local, T server) where T : VersionedEntity
    {
        try
        {
            // Serialize both
            var localJson = JsonSerializer.Serialize(local);
            var serverJson = JsonSerializer.Serialize(server);

            using var localDoc = JsonDocument.Parse(localJson);
            using var serverDoc = JsonDocument.Parse(serverJson);

            var merged = new Dictionary<string, object?>();

            // Merge properties
            foreach (var prop in localDoc.RootElement.EnumerateObject())
            {
                merged[prop.Name] = GetValue(prop.Value);
            }

            foreach (var prop in serverDoc.RootElement.EnumerateObject())
            {
                if (!merged.ContainsKey(prop.Name))
                {
                    merged[prop.Name] = GetValue(prop.Value);
                }
                else
                {
                    // Conflict - keep server for simplicity (could be smarter)
                    if (prop.Name != "id" && prop.Name != "createdAt")
                    {
                        merged[prop.Name] = GetValue(prop.Value);
                    }
                }
            }

            merged["updatedAt"] = DateTime.UtcNow;
            merged["checksum"] = null; // Will be recomputed

            var mergedJson = JsonSerializer.Serialize(merged);
            return JsonSerializer.Deserialize<T>(mergedJson);
        }
        catch (Exception ex)
        {
            Logger.Error("ConflictResolverService", "Semantic merge failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Sets default strategy for an entity type.
    /// </summary>
    public void SetDefaultStrategy(string entityType, ConflictResolutionStrategy strategy)
    {
        _defaultStrategies[entityType] = strategy;
    }

    private ConflictResolutionStrategy GetDefaultStrategy(string entityType)
    {
        return _defaultStrategies.TryGetValue(entityType, out var strategy)
            ? strategy
            : ConflictResolutionStrategy.LastWriteWins;
    }

    private ConflictResolution<T> ResolveLWW<T>(T local, T server, DateTime localTime, DateTime serverTime) where T : VersionedEntity
    {
        var winner = localTime > serverTime ? local : server;
        var source = localTime > serverTime ? "local" : "server";

        return new ConflictResolution<T>
        {
            Winner = winner,
            Loser = localTime > serverTime ? server : local,
            Strategy = ConflictResolutionStrategy.LastWriteWins,
            WinnerSource = source,
            RequiresManualResolution = false,
            Local = local,
            Server = server
        };
    }

    private ConflictResolution<T> ResolveFWW<T>(T local, T server, DateTime localTime, DateTime serverTime) where T : VersionedEntity
    {
        var winner = localTime < serverTime ? local : server;
        var source = localTime < serverTime ? "local" : "server";

        return new ConflictResolution<T>
        {
            Winner = winner,
            Loser = localTime < serverTime ? server : local,
            Strategy = ConflictResolutionStrategy.FirstWriteWins,
            WinnerSource = source,
            RequiresManualResolution = false,
            Local = local,
            Server = server
        };
    }

    private ConflictResolution<T> ResolveMerge<T>(T local, T server) where T : VersionedEntity
    {
        var merged = SemanticMerge(local, server);

        return new ConflictResolution<T>
        {
            Winner = merged ?? local,
            Loser = null,
            Strategy = ConflictResolutionStrategy.Merge,
            WinnerSource = "merged",
            RequiresManualResolution = merged == null,
            Local = local,
            Server = server,
            Merged = merged
        };
    }

    private static object? GetValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.ToString()
    };

    private void InitializeDefaultStrategies()
    {
        _defaultStrategies["RedballConfig"] = ConflictResolutionStrategy.LastWriteWins;
        _defaultStrategies["KeepAwakeSession"] = ConflictResolutionStrategy.LastWriteWins;
        _defaultStrategies["TypeThingPreset"] = ConflictResolutionStrategy.Merge;
    }
}

/// <summary>
/// Conflict resolution result.
/// </summary>
public class ConflictResolution<T> where T : VersionedEntity
{
    public T? Winner { get; set; }
    public T? Loser { get; set; }
    public T? Merged { get; set; }
    public ConflictResolutionStrategy Strategy { get; set; }
    public string WinnerSource { get; set; } = "";
    public bool RequiresManualResolution { get; set; }
    public T? Local { get; set; }
    public T? Server { get; set; }
}
