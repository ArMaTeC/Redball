using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class KeepAwakeServiceTests
    {
        [TestMethod]
        public void KeepAwakeService_Instance_IsNotNull()
        {
            // Act
            var instance = KeepAwakeService.Instance;

            // Assert
            Assert.IsNotNull(instance, "Instance should not be null");
        }

        [TestMethod]
        public void KeepAwakeService_DefaultValues_AreCorrect()
        {
            // Arrange
            var service = KeepAwakeService.Instance;

            // Assert
            Assert.IsFalse(service.IsActive, "Should not be active by default");
            Assert.IsTrue(service.PreventDisplaySleep, "Should prevent display sleep by default");
            Assert.IsTrue(service.UseHeartbeat, "Should use heartbeat by default");
            Assert.IsFalse(service.AutoPausedBattery, "Should not be auto-paused for battery by default");
            Assert.IsFalse(service.AutoPausedNetwork, "Should not be auto-paused for network by default");
            Assert.IsFalse(service.AutoPausedIdle, "Should not be auto-paused for idle by default");
            Assert.IsFalse(service.AutoPausedSchedule, "Should not be auto-paused for schedule by default");
            Assert.IsNull(service.Until, "Until should be null by default");
            Assert.IsNull(service.StartTime, "StartTime should be null by default");
        }

        [TestMethod]
        public void KeepAwakeService_PreventDisplaySleep_CanBeSet()
        {
            // Arrange
            var service = KeepAwakeService.Instance;
            var originalValue = service.PreventDisplaySleep;

            // Act
            service.PreventDisplaySleep = false;
            var newValue = service.PreventDisplaySleep;
            
            // Restore
            service.PreventDisplaySleep = originalValue;

            // Assert
            Assert.IsFalse(newValue, "PreventDisplaySleep should be settable");
        }

        [TestMethod]
        public void KeepAwakeService_UseHeartbeat_CanBeSet()
        {
            // Arrange
            var service = KeepAwakeService.Instance;

            // Act
            service.UseHeartbeat = false;

            // Assert
            Assert.IsFalse(service.UseHeartbeat, "UseHeartbeat should be settable");
        }

        [TestMethod]
        public void KeepAwakeService_StartTimed_SetsUntilTime()
        {
            // Arrange
            var service = KeepAwakeService.Instance;
            var beforeTime = DateTime.Now;

            // Act
            service.StartTimed(30);
            var until = service.Until;

            // Assert
            Assert.IsNotNull(until, "Until should be set after StartTimed");
            Assert.IsTrue(until > beforeTime.AddMinutes(29) && until < beforeTime.AddMinutes(31), 
                "Until should be approximately 30 minutes from now");
                
            // Cleanup
            service.SetActive(false);
        }

        [TestMethod]
        public void KeepAwakeService_StartTimed_InvalidDuration_ReturnsWithoutSetting()
        {
            // Arrange
            var service = KeepAwakeService.Instance;
            var originalUntil = service.Until;

            // Act
            service.StartTimed(0);  // Invalid - too low
            var afterLow = service.Until;
            
            service.StartTimed(800);  // Invalid - too high
            var afterHigh = service.Until;

            // Assert
            Assert.AreEqual(originalUntil, afterLow, "Until should not change with invalid low duration");
            Assert.AreEqual(originalUntil, afterHigh, "Until should not change with invalid high duration");
        }

        [TestMethod]
        public void KeepAwakeService_Toggle_SwitchesActiveState()
        {
            // Arrange
            var service = KeepAwakeService.Instance;
            service.SetActive(false);
            var wasActive = service.IsActive;

            // Act
            service.Toggle();
            var isActive = service.IsActive;

            // Assert
            Assert.AreNotEqual(wasActive, isActive, "Toggle should switch active state");
            Assert.IsTrue(isActive, "Should be active after toggle from inactive");
            
            // Cleanup
            service.SetActive(false);
        }

        [TestMethod]
        public void KeepAwakeService_GetStatusText_WhenInactive_ReturnsCorrectText()
        {
            // Arrange
            var service = KeepAwakeService.Instance;
            service.SetActive(false);

            // Act
            var status = service.GetStatusText();

            // Assert
            Assert.AreEqual("Paused | Display Normal | Heartbeat Off", status);
        }

        [TestMethod]
        public void KeepAwakeService_AutoPause_OnlyWorksWhenActive()
        {
            // Arrange
            var service = KeepAwakeService.Instance;
            service.SetActive(false);

            // Act - try to auto-pause when inactive
            service.AutoPause("Battery");

            // Assert
            Assert.IsFalse(service.AutoPausedBattery, "Should not auto-pause when inactive");
        }
    }
}
