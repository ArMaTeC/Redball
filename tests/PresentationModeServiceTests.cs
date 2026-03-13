using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class PresentationModeServiceTests
    {
        [TestMethod]
        public void PresentationModeService_DefaultValues_AreCorrect()
        {
            // Arrange
            var service = new PresentationModeService();

            // Assert
            Assert.IsFalse(service.IsEnabled, "Should be disabled by default");
        }

        [TestMethod]
        public void PresentationModeService_IsEnabled_CanBeToggled()
        {
            // Arrange
            var service = new PresentationModeService();

            // Act
            service.IsEnabled = true;

            // Assert
            Assert.IsTrue(service.IsEnabled, "Should be enabled after toggle");
        }

        [TestMethod]
        public void PresentationStatus_DefaultValues_AreCorrect()
        {
            // Arrange
            var status = new PresentationStatus();

            // Assert
            Assert.IsFalse(status.IsPresenting, "Should not be presenting by default");
            Assert.AreEqual("", status.Source, "Source should be empty by default");
        }

        [TestMethod]
        public void PresentationStatus_Properties_CanBeSet()
        {
            // Arrange
            var status = new PresentationStatus
            {
                IsPresenting = true,
                Source = "PowerPoint"
            };

            // Assert
            Assert.IsTrue(status.IsPresenting, "IsPresenting should be settable");
            Assert.AreEqual("PowerPoint", status.Source, "Source should be settable");
        }
    }
}
