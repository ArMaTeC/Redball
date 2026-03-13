using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class AnalyticsServiceTests
    {
        [TestMethod]
        public void AnalyticsService_TrackFeature_IncrementsCount()
        {
            // Arrange
            var service = new AnalyticsService(true);
            var featureName = "TestFeature";

            // Act
            service.TrackFeature(featureName);
            service.TrackFeature(featureName);
            var summary = service.GetSummary();

            // Assert
            var feature = summary.TopFeatures.Find(f => f.Name == featureName);
            Assert.IsNotNull(feature, "Feature should be tracked");
            Assert.AreEqual(2, feature.Count, "Feature count should be 2");
        }

        [TestMethod]
        public void AnalyticsService_TrackFeature_WithContext_TracksContext()
        {
            // Arrange
            var service = new AnalyticsService(true);

            // Act
            service.TrackFeature("KeepAwake", "ManualToggle");
            service.TrackFeature("KeepAwake", "KeyboardShortcut");
            var summary = service.GetSummary();

            // Assert
            Assert.IsTrue(summary.TopFeatures.Count > 0, "Should have tracked features");
        }

        [TestMethod]
        public void AnalyticsService_TrackSession_IncrementsSessions()
        {
            // Arrange
            var service = new AnalyticsService(true);
            var initialSessions = service.GetSummary().TotalSessions;

            // Act
            service.TrackSessionStart();
            service.TrackSessionEnd();
            var summary = service.GetSummary();

            // Assert
            Assert.AreEqual(initialSessions + 1, summary.TotalSessions, "Should increment session count");
        }

        [TestMethod]
        public void AnalyticsService_Export_ReturnsValidJson()
        {
            // Arrange
            var service = new AnalyticsService(true);
            service.TrackFeature("Test");

            // Act
            var json = service.Export();

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(json), "Export should not be empty");
            Assert.IsTrue(json.Contains("Features"), "Export should contain Features");
        }

        [TestMethod]
        public void AnalyticsService_Disabled_DoesNotTrack()
        {
            // Arrange
            var service = new AnalyticsService(false);

            // Act
            service.TrackFeature("ShouldNotTrack");
            var summary = service.GetSummary();

            // Assert
            Assert.AreEqual(0, summary.TopFeatures.Count, "Disabled service should not track features");
        }
    }
}
