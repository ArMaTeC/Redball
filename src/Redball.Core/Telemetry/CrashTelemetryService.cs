namespace Redball.Core.Telemetry;

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Collects, stores, and uploads crash telemetry with privacy-safe PII scrubbing.
/// Implements retry logic and consent-based reporting with first-run opt-in flow.
/// </summary>
public sealed class CrashTelemetryService
{
    private static readonly Lazy<CrashTelemetryService> _instance = new(() => new CrashTelemetryService());
    public static CrashTelemetryService Instance => _instance.Value;

    private readonly string _crashStorePath;
    private readonly string _uploadQueuePath;
    private readonly string _consentFilePath;
    private bool _consentGranted;
    private bool _consentConfigured;
    private string? _endpointUrl;
    private string? _apiKey;

    private CrashTelemetryService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var redballDir = Path.Combine(localAppData, "Redball", "UserData", "Telemetry");
        Directory.CreateDirectory(redballDir);

        _crashStorePath = Path.Combine(redballDir, "crashes");
        _uploadQueuePath = Path.Combine(redballDir, "upload_queue");
        _consentFilePath = Path.Combine(redballDir, "consent.json");

        Directory.CreateDirectory(_crashStorePath);
        Directory.CreateDirectory(_uploadQueuePath);

        LoadConsentConfiguration();
    }

    /// <summary>
    /// User consent for crash reporting. Must be explicitly granted.
    /// </summary>
    public bool ConsentGranted
    {
        get => _consentGranted;
        set
        {
            _consentGranted = value;
            _consentConfigured = true;
            SaveConsentConfiguration();
        }
    }

    /// <summary>
    /// Whether the user has made a consent decision (opt-in or opt-out).
    /// If false, first-run consent dialog should be shown.
    /// </summary>
    public bool IsConsentConfigured => _consentConfigured;

    /// <summary>
    /// Loads consent configuration from disk.
    /// </summary>
    private void LoadConsentConfiguration()
    {
        try
        {
            if (File.Exists(_consentFilePath))
            {
                var json = File.ReadAllText(_consentFilePath);
                var config = JsonSerializer.Deserialize<ConsentConfig>(json);
                if (config != null)
                {
                    _consentGranted = config.ConsentGranted;
                    _consentConfigured = config.ConsentConfigured;
                }
            }
        }
        catch
        {
            // If we can't read consent file, treat as unconfigured
            _consentGranted = false;
            _consentConfigured = false;
        }
    }

    /// <summary>
    /// Persists consent configuration to disk.
    /// </summary>
    private void SaveConsentConfiguration()
    {
        try
        {
            var config = new ConsentConfig
            {
                ConsentGranted = _consentGranted,
                ConsentConfigured = _consentConfigured,
                ConfiguredAt = DateTime.UtcNow
            };
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_consentFilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Error("CrashTelemetry", "Failed to save consent configuration", ex);
        }
    }

    /// <summary>
    /// Resets consent configuration to show dialog again (for testing or privacy resets).
    /// </summary>
    public void ResetConsent()
    {
        _consentGranted = false;
        _consentConfigured = false;
        try
        {
            if (File.Exists(_consentFilePath))
            {
                File.Delete(_consentFilePath);
            }
        }
        catch { }
    }

    /// <summary>
    /// Configuration for consent persistence.
    /// </summary>
    private class ConsentConfig
    {
        public bool ConsentGranted { get; set; }
        public bool ConsentConfigured { get; set; }
        public DateTime ConfiguredAt { get; set; }
    }

    /// <summary>
    /// Configures the telemetry endpoint.
    /// </summary>
    public void Configure(string endpointUrl, string apiKey)
    {
        _endpointUrl = endpointUrl;
        _apiKey = apiKey;
    }

    /// <summary>
    /// Records a crash to local storage and queues for upload if consented.
    /// Call this from global exception handlers.
    /// </summary>
    public void RecordCrash(Exception ex, Dictionary<string, string>? tags = null)
    {
        try
        {
            var version = GetAppVersion();
            var channel = GetChannel();

            var envelope = CrashEnvelope.FromException(ex, version, channel, tags);

            // Save to local crash store
            var crashFile = Path.Combine(_crashStorePath, $"{envelope.ReportId}.json");
            var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(crashFile, json);

            Logger.Error("CrashTelemetry", $"Crash recorded: {envelope.ExceptionType} (Fingerprint: {envelope.StackFingerprint})", ex);

            // Queue for upload if user consented
            if (_consentGranted && !string.IsNullOrEmpty(_endpointUrl))
            {
                var queueFile = Path.Combine(_uploadQueuePath, $"{envelope.ReportId}.json");
                File.WriteAllText(queueFile, json);
            }
        }
        catch (Exception telemetryEx)
        {
            // Don't let telemetry failures mask the original crash
            Logger.Error("CrashTelemetry", "Failed to record crash telemetry", telemetryEx);
        }
    }

    /// <summary>
    /// Uploads queued crash reports on next startup.
    /// Call this early in application startup.
    /// </summary>
    public async Task UploadQueuedReportsAsync(CancellationToken ct = default)
    {
        if (!_consentGranted || string.IsNullOrEmpty(_endpointUrl))
        {
            return;
        }

        var queueFiles = Directory.GetFiles(_uploadQueuePath, "*.json");
        if (queueFiles.Length == 0)
        {
            return;
        }

        Logger.Info("CrashTelemetry", $"Uploading {queueFiles.Length} queued crash reports...");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("X-API-Key", _apiKey);

        foreach (var file in queueFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"{_endpointUrl}/api/v1/crashes", content, ct);

                if (response.IsSuccessStatusCode)
                {
                    // Upload successful - remove from queue
                    File.Delete(file);
                    Logger.Debug("CrashTelemetry", $"Uploaded crash report: {Path.GetFileName(file)}");
                }
                else
                {
                    // Keep in queue for retry
                    Logger.Warning("CrashTelemetry", $"Upload failed for {Path.GetFileName(file)}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("CrashTelemetry", $"Error uploading {Path.GetFileName(file)}", ex);
                // Keep in queue for next startup retry
            }
        }
    }

    /// <summary>
    /// Gets locally stored crash reports for diagnostics export.
    /// </summary>
    public IReadOnlyList<CrashEnvelope> GetLocalCrashes(int limit = 50)
    {
        var crashes = new List<CrashEnvelope>();
        var files = Directory.GetFiles(_crashStorePath, "*.json")
            .OrderByDescending(File.GetLastWriteTime)
            .Take(limit);

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var envelope = JsonSerializer.Deserialize<CrashEnvelope>(json);
                if (envelope != null)
                {
                    crashes.Add(envelope);
                }
            }
            catch
            {
                // Skip corrupted files
            }
        }

        return crashes;
    }

    /// <summary>
    /// Creates a diagnostics bundle for support (logs + recent crashes + config metadata).
    /// </summary>
    public async Task<string> CreateDiagnosticsBundleAsync(CancellationToken ct = default)
    {
        var bundleDir = Path.Combine(Path.GetTempPath(), $"redball_diagnostics_{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundleDir);

        // Copy recent crash reports (max 10)
        var crashes = GetLocalCrashes(10);
        var crashesJson = JsonSerializer.Serialize(crashes, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(bundleDir, "crashes.json"), crashesJson, ct);

        // Create metadata file
        var metadata = new
        {
            GeneratedAt = DateTime.UtcNow,
            AppVersion = GetAppVersion(),
            Channel = GetChannel(),
            OsVersion = Environment.OSVersion.ToString(),
            Is64Bit = Environment.Is64BitProcess,
            DotNetVersion = Environment.Version.ToString(),
            CrashCount = Directory.GetFiles(_crashStorePath, "*.json").Length,
            PendingUploads = Directory.GetFiles(_uploadQueuePath, "*.json").Length,
            ConsentGranted = _consentGranted
        };
        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(bundleDir, "metadata.json"), metadataJson, ct);

        // Note: Log files would be copied here in a real implementation
        // but require knowing the actual log file paths from the logging system

        var zipPath = $"{bundleDir}.zip";
        System.IO.Compression.ZipFile.CreateFromDirectory(bundleDir, zipPath);
        Directory.Delete(bundleDir, true);

        return zipPath;
    }

    /// <summary>
    /// Purges old crash reports beyond retention period.
    /// </summary>
    public int PurgeOldCrashes(TimeSpan retention)
    {
        var cutoff = DateTime.UtcNow - retention;
        var purged = 0;

        foreach (var file in Directory.GetFiles(_crashStorePath, "*.json"))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < cutoff)
                {
                    File.Delete(file);
                    purged++;
                }
            }
            catch
            {
                // Skip files we can't delete
            }
        }

        return purged;
    }

    private static string GetAppVersion()
    {
        // In a real app, read from assembly version
        return typeof(CrashTelemetryService).Assembly.GetName().Version?.ToString() ?? "3.0.0";
    }

    private static string GetChannel()
    {
        // In a real app, read from config
        return "stable";
    }
}
