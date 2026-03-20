using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.IO;
using System.Text.Json;

namespace Redball.Tests
{
    [TestClass]
    public class ConfigBoundaryTests
    {
        private string _tempDir = "";

        [TestInitialize]
        public void TestInitialize()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"redball_boundary_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);

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

        // --- HeartbeatSeconds boundaries: valid range [10, 300] ---

        [TestMethod]
        public void Validate_HeartbeatSeconds_AtLowerBound_NoError()
        {
            ConfigService.Instance.Config.HeartbeatSeconds = 10;
            var errors = ConfigService.Instance.Validate();
            Assert.IsFalse(errors.Exists(e => e.Contains("HeartbeatSeconds")),
                "HeartbeatSeconds=10 should be valid");
        }

        [TestMethod]
        public void Validate_HeartbeatSeconds_BelowLowerBound_HasError()
        {
            ConfigService.Instance.Config.HeartbeatSeconds = 9;
            var errors = ConfigService.Instance.Validate();
            Assert.IsTrue(errors.Exists(e => e.Contains("HeartbeatSeconds")),
                "HeartbeatSeconds=9 should be invalid");
        }

        [TestMethod]
        public void Validate_HeartbeatSeconds_AtUpperBound_NoError()
        {
            ConfigService.Instance.Config.HeartbeatSeconds = 300;
            var errors = ConfigService.Instance.Validate();
            Assert.IsFalse(errors.Exists(e => e.Contains("HeartbeatSeconds")),
                "HeartbeatSeconds=300 should be valid");
        }

        [TestMethod]
        public void Validate_HeartbeatSeconds_AboveUpperBound_HasError()
        {
            ConfigService.Instance.Config.HeartbeatSeconds = 301;
            var errors = ConfigService.Instance.Validate();
            Assert.IsTrue(errors.Exists(e => e.Contains("HeartbeatSeconds")),
                "HeartbeatSeconds=301 should be invalid");
        }

        // --- BatteryThreshold boundaries: valid range [5, 95] ---

        [TestMethod]
        public void Validate_BatteryThreshold_AtLowerBound_NoError()
        {
            ConfigService.Instance.Config.BatteryThreshold = 5;
            var errors = ConfigService.Instance.Validate();
            Assert.IsFalse(errors.Exists(e => e.Contains("BatteryThreshold")),
                "BatteryThreshold=5 should be valid");
        }

        [TestMethod]
        public void Validate_BatteryThreshold_BelowLowerBound_HasError()
        {
            ConfigService.Instance.Config.BatteryThreshold = 4;
            var errors = ConfigService.Instance.Validate();
            Assert.IsTrue(errors.Exists(e => e.Contains("BatteryThreshold")),
                "BatteryThreshold=4 should be invalid");
        }

        [TestMethod]
        public void Validate_BatteryThreshold_AtUpperBound_NoError()
        {
            ConfigService.Instance.Config.BatteryThreshold = 95;
            var errors = ConfigService.Instance.Validate();
            Assert.IsFalse(errors.Exists(e => e.Contains("BatteryThreshold")),
                "BatteryThreshold=95 should be valid");
        }

        [TestMethod]
        public void Validate_BatteryThreshold_AboveUpperBound_HasError()
        {
            ConfigService.Instance.Config.BatteryThreshold = 96;
            var errors = ConfigService.Instance.Validate();
            Assert.IsTrue(errors.Exists(e => e.Contains("BatteryThreshold")),
                "BatteryThreshold=96 should be invalid");
        }

        // --- DefaultDuration boundaries: valid range [1, 720] ---

        [TestMethod]
        public void Validate_DefaultDuration_AtLowerBound_NoError()
        {
            ConfigService.Instance.Config.DefaultDuration = 1;
            var errors = ConfigService.Instance.Validate();
            Assert.IsFalse(errors.Exists(e => e.Contains("DefaultDuration")),
                "DefaultDuration=1 should be valid");
        }

        [TestMethod]
        public void Validate_DefaultDuration_BelowLowerBound_HasError()
        {
            ConfigService.Instance.Config.DefaultDuration = 0;
            var errors = ConfigService.Instance.Validate();
            Assert.IsTrue(errors.Exists(e => e.Contains("DefaultDuration")),
                "DefaultDuration=0 should be invalid");
        }

        [TestMethod]
        public void Validate_DefaultDuration_AtUpperBound_NoError()
        {
            ConfigService.Instance.Config.DefaultDuration = 720;
            var errors = ConfigService.Instance.Validate();
            Assert.IsFalse(errors.Exists(e => e.Contains("DefaultDuration")),
                "DefaultDuration=720 should be valid");
        }

        [TestMethod]
        public void Validate_DefaultDuration_AboveUpperBound_HasError()
        {
            ConfigService.Instance.Config.DefaultDuration = 721;
            var errors = ConfigService.Instance.Validate();
            Assert.IsTrue(errors.Exists(e => e.Contains("DefaultDuration")),
                "DefaultDuration=721 should be invalid");
        }

        // --- TypeThingStartDelaySec boundaries: valid range [0, 30] ---

        [TestMethod]
        public void Validate_TypeThingStartDelaySec_AtLowerBound_NoError()
        {
            ConfigService.Instance.Config.TypeThingStartDelaySec = 0;
            var errors = ConfigService.Instance.Validate();
            Assert.IsFalse(errors.Exists(e => e.Contains("TypeThingStartDelaySec")),
                "TypeThingStartDelaySec=0 should be valid");
        }

        [TestMethod]
        public void Validate_TypeThingStartDelaySec_AtUpperBound_NoError()
        {
            ConfigService.Instance.Config.TypeThingStartDelaySec = 30;
            var errors = ConfigService.Instance.Validate();
            Assert.IsFalse(errors.Exists(e => e.Contains("TypeThingStartDelaySec")),
                "TypeThingStartDelaySec=30 should be valid");
        }

        [TestMethod]
        public void Validate_TypeThingStartDelaySec_AboveUpperBound_HasError()
        {
            ConfigService.Instance.Config.TypeThingStartDelaySec = 31;
            var errors = ConfigService.Instance.Validate();
            Assert.IsTrue(errors.Exists(e => e.Contains("TypeThingStartDelaySec")),
                "TypeThingStartDelaySec=31 should be invalid");
        }

        // --- MaxLogSizeMB boundaries: valid range [1, 100] ---

        [TestMethod]
        public void Validate_MaxLogSizeMB_AtLowerBound_NoError()
        {
            ConfigService.Instance.Config.MaxLogSizeMB = 1;
            var errors = ConfigService.Instance.Validate();
            Assert.IsFalse(errors.Exists(e => e.Contains("MaxLogSizeMB")),
                "MaxLogSizeMB=1 should be valid");
        }

        [TestMethod]
        public void Validate_MaxLogSizeMB_BelowLowerBound_HasError()
        {
            ConfigService.Instance.Config.MaxLogSizeMB = 0;
            var errors = ConfigService.Instance.Validate();
            Assert.IsTrue(errors.Exists(e => e.Contains("MaxLogSizeMB")),
                "MaxLogSizeMB=0 should be invalid");
        }

        [TestMethod]
        public void Validate_MaxLogSizeMB_AtUpperBound_NoError()
        {
            ConfigService.Instance.Config.MaxLogSizeMB = 100;
            var errors = ConfigService.Instance.Validate();
            Assert.IsFalse(errors.Exists(e => e.Contains("MaxLogSizeMB")),
                "MaxLogSizeMB=100 should be valid");
        }

        [TestMethod]
        public void Validate_MaxLogSizeMB_AboveUpperBound_HasError()
        {
            ConfigService.Instance.Config.MaxLogSizeMB = 101;
            var errors = ConfigService.Instance.Validate();
            Assert.IsTrue(errors.Exists(e => e.Contains("MaxLogSizeMB")),
                "MaxLogSizeMB=101 should be invalid");
        }

        // --- TypeThing delay relationship ---

        [TestMethod]
        public void Validate_TypeThingDelays_MinEqualsMax_HasError()
        {
            ConfigService.Instance.Config.TypeThingMinDelayMs = 100;
            ConfigService.Instance.Config.TypeThingMaxDelayMs = 100;
            var errors = ConfigService.Instance.Validate();
            Assert.IsTrue(errors.Exists(e => e.Contains("TypeThingMinDelayMs")),
                "MinDelay == MaxDelay should be invalid");
        }

        [TestMethod]
        public void Validate_TypeThingDelays_MinLessThanMax_NoError()
        {
            ConfigService.Instance.Config.TypeThingMinDelayMs = 30;
            ConfigService.Instance.Config.TypeThingMaxDelayMs = 31;
            var errors = ConfigService.Instance.Validate();
            Assert.IsFalse(errors.Exists(e => e.Contains("TypeThingMinDelayMs")),
                "MinDelay < MaxDelay should be valid");
        }

        // --- Multiple errors at once ---

        [TestMethod]
        public void Validate_MultipleInvalidValues_ReturnsMultipleErrors()
        {
            ConfigService.Instance.Config.HeartbeatSeconds = 1;
            ConfigService.Instance.Config.DefaultDuration = 0;
            ConfigService.Instance.Config.BatteryThreshold = 100;
            ConfigService.Instance.Config.MaxLogSizeMB = 0;

            var errors = ConfigService.Instance.Validate();
            Assert.IsTrue(errors.Count >= 4, $"Should have at least 4 errors, got {errors.Count}");
        }
    }
}
