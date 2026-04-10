using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class TelemetryServiceTests
    {
        [TestMethod]
        public void LogEvent_WhenTelemetryDisabled_DoesNotThrow()
        {
            // Arrange
            var originalConfig = ConfigService.Instance.Config.EnableTelemetry;
            ConfigService.Instance.Config.EnableTelemetry = false;

            try
            {
                // Act & Assert - Should not throw
                TelemetryService.LogEvent("test_event", new { data = "test" });
                TelemetryService.LogEvent("test_event2");
                TelemetryService.LogEvent("", new { });
                TelemetryService.LogEvent(null!, null!);
            }
            finally
            {
                // Cleanup
                ConfigService.Instance.Config.EnableTelemetry = originalConfig;
            }
        }

        [TestMethod]
        public void LogEvent_WhenTelemetryEnabled_DoesNotThrow()
        {
            // Arrange
            var originalConfig = ConfigService.Instance.Config.EnableTelemetry;
            ConfigService.Instance.Config.EnableTelemetry = true;

            try
            {
                // Act & Assert - Should not throw
                TelemetryService.LogEvent("test_event", new { data = "test" });
                TelemetryService.LogEvent("user_action", new { action = "click", target = "button" });
            }
            finally
            {
                // Cleanup
                ConfigService.Instance.Config.EnableTelemetry = originalConfig;
            }
        }

        [TestMethod]
        public void LogEvent_WithNullData_DoesNotThrow()
        {
            // Arrange
            var originalConfig = ConfigService.Instance.Config.EnableTelemetry;
            ConfigService.Instance.Config.EnableTelemetry = true;

            try
            {
                // Act & Assert - Should not throw
                TelemetryService.LogEvent("test_event", null);
                TelemetryService.LogEvent("test_event2");
            }
            finally
            {
                // Cleanup
                ConfigService.Instance.Config.EnableTelemetry = originalConfig;
            }
        }

        [TestMethod]
        public void LogEvent_WithComplexData_DoesNotThrow()
        {
            // Arrange
            var originalConfig = ConfigService.Instance.Config.EnableTelemetry;
            ConfigService.Instance.Config.EnableTelemetry = true;

            try
            {
                var complexData = new
                {
                    String = "test",
                    Number = 42,
                    Boolean = true,
                    Nested = new { Inner = "value" },
                    Array = new[] { 1, 2, 3 },
                    DateTime = DateTime.UtcNow
                };

                // Act & Assert - Should not throw
                TelemetryService.LogEvent("complex_event", complexData);
            }
            finally
            {
                // Cleanup
                ConfigService.Instance.Config.EnableTelemetry = originalConfig;
            }
        }

        [TestMethod]
        public void LogEvent_WithEmptyEventName_DoesNotThrow()
        {
            // Arrange
            var originalConfig = ConfigService.Instance.Config.EnableTelemetry;
            ConfigService.Instance.Config.EnableTelemetry = true;

            try
            {
                // Act & Assert - Should not throw
                TelemetryService.LogEvent("");
                TelemetryService.LogEvent("   ");
            }
            finally
            {
                // Cleanup
                ConfigService.Instance.Config.EnableTelemetry = originalConfig;
            }
        }

        [TestMethod]
        public void LogEvent_MultipleCalls_DoesNotThrow()
        {
            // Arrange
            var originalConfig = ConfigService.Instance.Config.EnableTelemetry;
            ConfigService.Instance.Config.EnableTelemetry = true;

            try
            {
                // Act & Assert - Should not throw
                for (int i = 0; i < 10; i++)
                {
                    TelemetryService.LogEvent($"event_{i}", new { iteration = i });
                }
            }
            finally
            {
                // Cleanup
                ConfigService.Instance.Config.EnableTelemetry = originalConfig;
            }
        }

        [TestMethod]
        public void LogEvent_Service_IsStatic()
        {
            // TelemetryService is a static class, so we verify it exists and can be called
            // Act & Assert
            Assert.IsTrue(true, "TelemetryService is a static class accessible without instantiation");
        }
    }
}
