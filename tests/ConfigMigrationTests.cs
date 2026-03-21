using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.IO;
using System.Text.Json;

namespace Redball.Tests
{
    [TestClass]
    public class ConfigMigrationTests
    {
        private string _tempDir = "";

        [TestInitialize]
        public void TestInitialize()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"redball_migration_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);

            // Isolate from real LocalAppData config persistence logic during tests
            ConfigService.Instance.IsTestMode = true;

            // Reset Config to defaults before each test
            var resetPath = Path.Combine(_tempDir, "reset_defaults.json");
            File.WriteAllText(resetPath, JsonSerializer.Serialize(new RedballConfig()));
            ConfigService.Instance.Load(resetPath);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        [TestMethod]
        public void Load_MigratesLegacyRepoOwner_ToArMaTeC()
        {
            // Arrange — simulate a config from the old "karl-lawrence" repo
            var legacyConfig = new RedballConfig
            {
                UpdateRepoOwner = "karl-lawrence",
                UpdateRepoName = "Redball"
            };
            var path = Path.Combine(_tempDir, "legacy_repo.json");
            File.WriteAllText(path, JsonSerializer.Serialize(legacyConfig));

            // Act
            ConfigService.Instance.Load(path);

            // Assert — NormalizeConfig should have migrated it
            Assert.AreEqual("ArMaTeC", ConfigService.Instance.Config.UpdateRepoOwner,
                "Legacy repo owner should be migrated to ArMaTeC");
            Assert.AreEqual("Redball", ConfigService.Instance.Config.UpdateRepoName);
        }

        [TestMethod]
        public void Load_EmptyRepoOwner_DefaultsToArMaTeC()
        {
            // Arrange
            var config = new RedballConfig { UpdateRepoOwner = "", UpdateRepoName = "" };
            var path = Path.Combine(_tempDir, "empty_repo.json");
            File.WriteAllText(path, JsonSerializer.Serialize(config));

            // Act
            ConfigService.Instance.Load(path);

            // Assert
            Assert.AreEqual("ArMaTeC", ConfigService.Instance.Config.UpdateRepoOwner);
            Assert.AreEqual("Redball", ConfigService.Instance.Config.UpdateRepoName);
        }

        [TestMethod]
        public void Load_InvalidHeartbeatInputMode_DefaultsToF15()
        {
            // Arrange
            var config = new RedballConfig { HeartbeatInputMode = "InvalidMode" };
            var path = Path.Combine(_tempDir, "bad_input_mode.json");
            File.WriteAllText(path, JsonSerializer.Serialize(config));

            // Act
            ConfigService.Instance.Load(path);

            // Assert
            Assert.AreEqual("F15", ConfigService.Instance.Config.HeartbeatInputMode,
                "Invalid HeartbeatInputMode should be normalized to F15");
        }

        [TestMethod]
        public void Load_EmptyHeartbeatInputMode_WithUseHeartbeatTrue_SetsF15()
        {
            // Arrange
            var config = new RedballConfig
            {
                HeartbeatInputMode = "",
                UseHeartbeatKeypress = true
            };
            var path = Path.Combine(_tempDir, "empty_input_mode.json");
            File.WriteAllText(path, JsonSerializer.Serialize(config));

            // Act
            ConfigService.Instance.Load(path);

            // Assert
            Assert.AreEqual("F15", ConfigService.Instance.Config.HeartbeatInputMode);
            Assert.IsTrue(ConfigService.Instance.Config.UseHeartbeatKeypress);
        }

        [TestMethod]
        public void Load_DisabledHeartbeatInputMode_SyncsUseHeartbeatKeypress()
        {
            // Arrange
            var config = new RedballConfig
            {
                HeartbeatInputMode = "Disabled",
                UseHeartbeatKeypress = true // conflicts with Disabled
            };
            var path = Path.Combine(_tempDir, "disabled_mode.json");
            File.WriteAllText(path, JsonSerializer.Serialize(config));

            // Act
            ConfigService.Instance.Load(path);

            // Assert — NormalizeConfig should sync UseHeartbeatKeypress to false
            Assert.AreEqual("Disabled", ConfigService.Instance.Config.HeartbeatInputMode);
            Assert.IsFalse(ConfigService.Instance.Config.UseHeartbeatKeypress,
                "UseHeartbeatKeypress should be false when HeartbeatInputMode is Disabled");
        }

        [TestMethod]
        public void Load_PropertyRecovery_PartiallyCorruptJson()
        {
            // Arrange — JSON with some valid and some corrupt properties
            var json = """
            {
                "HeartbeatSeconds": 45,
                "Theme": "OceanBlue",
                "DefaultDuration": "not_a_number",
                "BatteryThreshold": 30
            }
            """;
            var path = Path.Combine(_tempDir, "partial_corrupt.json");
            File.WriteAllText(path, json);

            // Act
            var result = ConfigService.Instance.Load(path);

            // Assert — valid properties should be recovered, invalid get defaults
            Assert.IsTrue(result, "Load should succeed via property-level recovery");
            Assert.AreEqual(45, ConfigService.Instance.Config.HeartbeatSeconds,
                "Valid HeartbeatSeconds should be recovered");
            Assert.AreEqual("OceanBlue", ConfigService.Instance.Config.Theme,
                "Valid Theme should be recovered");
            Assert.AreEqual(30, ConfigService.Instance.Config.BatteryThreshold,
                "Valid BatteryThreshold should be recovered");
        }

        [TestMethod]
        public void Load_SanitizesOutOfRangeValues()
        {
            // Arrange — config with all out-of-range values
            var config = new RedballConfig
            {
                HeartbeatSeconds = 1,        // < 10
                DefaultDuration = 0,          // < 1
                BatteryThreshold = 99,        // > 95
                MaxLogSizeMB = 0,             // < 1
                IdleThreshold = 0,            // < 1
                TypeThingMinDelayMs = 0,      // < 1
                TypeThingStartDelaySec = 50   // > 30
            };
            var path = Path.Combine(_tempDir, "out_of_range.json");
            File.WriteAllText(path, JsonSerializer.Serialize(config));

            // Act
            ConfigService.Instance.Load(path);

            // Assert — all should be clamped/reset to defaults
            var defaults = new RedballConfig();
            Assert.AreEqual(defaults.HeartbeatSeconds, ConfigService.Instance.Config.HeartbeatSeconds);
            Assert.AreEqual(defaults.DefaultDuration, ConfigService.Instance.Config.DefaultDuration);
            Assert.AreEqual(defaults.BatteryThreshold, ConfigService.Instance.Config.BatteryThreshold);
            Assert.AreEqual(defaults.MaxLogSizeMB, ConfigService.Instance.Config.MaxLogSizeMB);
            Assert.AreEqual(defaults.IdleThreshold, ConfigService.Instance.Config.IdleThreshold);
            Assert.AreEqual(defaults.TypeThingMinDelayMs, ConfigService.Instance.Config.TypeThingMinDelayMs);
            Assert.AreEqual(defaults.TypeThingStartDelaySec, ConfigService.Instance.Config.TypeThingStartDelaySec);
        }

        [TestMethod]
        public void Load_NullStringProperties_GetDefaults()
        {
            // Arrange — JSON with null/missing string properties
            var json = """
            {
                "HeartbeatSeconds": 59,
                "LogPath": null,
                "Locale": null,
                "Theme": null,
                "ScheduleStartTime": null,
                "ScheduleStopTime": null
            }
            """;
            var path = Path.Combine(_tempDir, "null_strings.json");
            File.WriteAllText(path, json);

            // Act
            ConfigService.Instance.Load(path);

            // Assert — null strings should be filled with defaults
            var defaults = new RedballConfig();
            Assert.AreEqual(defaults.LogPath, ConfigService.Instance.Config.LogPath);
            Assert.AreEqual(defaults.Locale, ConfigService.Instance.Config.Locale);
            Assert.AreEqual(defaults.Theme, ConfigService.Instance.Config.Theme);
            Assert.AreEqual(defaults.ScheduleStartTime, ConfigService.Instance.Config.ScheduleStartTime);
            Assert.AreEqual(defaults.ScheduleStopTime, ConfigService.Instance.Config.ScheduleStopTime);
        }

        [TestMethod]
        public void SaveAndLoad_RoundTrip_PreservesAllValues()
        {
            // Arrange — set non-default values
            var service = ConfigService.Instance;
            service.Config.HeartbeatSeconds = 42;
            service.Config.Theme = "MidnightBlue";
            service.Config.DefaultDuration = 120;
            service.Config.BatteryThreshold = 30;
            service.Config.NetworkAware = true;
            service.Config.IdleDetection = true;
            service.Config.IdleThreshold = 45;
            service.Config.TypeThingMinDelayMs = 50;
            service.Config.TypeThingMaxDelayMs = 200;

            var savePath = Path.Combine(_tempDir, "roundtrip.json");

            // Act — save and reload
            var saveResult = service.Save(savePath);
            Assert.IsTrue(saveResult, "Save should succeed");

            var loadResult = service.Load(savePath);
            Assert.IsTrue(loadResult, "Load should succeed");

            // Assert — all values should survive the round-trip
            Assert.AreEqual(42, service.Config.HeartbeatSeconds);
            Assert.AreEqual("MidnightBlue", service.Config.Theme);
            Assert.AreEqual(120, service.Config.DefaultDuration);
            Assert.AreEqual(30, service.Config.BatteryThreshold);
            Assert.IsTrue(service.Config.NetworkAware);
            Assert.IsTrue(service.Config.IdleDetection);
            Assert.AreEqual(45, service.Config.IdleThreshold);
            Assert.AreEqual(50, service.Config.TypeThingMinDelayMs);
            Assert.AreEqual(200, service.Config.TypeThingMaxDelayMs);
        }
    }
}
