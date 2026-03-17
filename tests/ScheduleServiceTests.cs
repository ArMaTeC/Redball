using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.Collections.Generic;

namespace Redball.Tests
{
    [TestClass]
    public class ScheduleServiceTests
    {
        [TestMethod]
        public void ScheduleService_IsInSchedule_Disabled_ReturnsFalse()
        {
            // Arrange
            var service = new ScheduleService
            {
                IsEnabled = false,
                StartTime = "00:00",
                StopTime = "23:59",
                Days = new List<string> { DateTime.Now.DayOfWeek.ToString() }
            };

            // Act
            var result = service.IsInSchedule();

            // Assert
            Assert.IsFalse(result, "Should return false when disabled");
        }

        [TestMethod]
        public void ScheduleService_IsInSchedule_WrongDay_ReturnsFalse()
        {
            // Arrange - set days to exclude today
            var today = DateTime.Now.DayOfWeek;
            var wrongDay = today == DayOfWeek.Monday ? DayOfWeek.Tuesday : DayOfWeek.Monday;
            
            var service = new ScheduleService
            {
                IsEnabled = true,
                StartTime = "00:00",
                StopTime = "23:59",
                Days = new List<string> { wrongDay.ToString() }
            };

            // Act
            var result = service.IsInSchedule();

            // Assert
            Assert.IsFalse(result, "Should return false when today is not in scheduled days");
        }

        [TestMethod]
        public void ScheduleService_IsInSchedule_InvalidTimeFormat_ReturnsFalse()
        {
            // Arrange
            var service = new ScheduleService
            {
                IsEnabled = true,
                StartTime = "invalid",
                StopTime = "also-invalid",
                Days = new List<string> { DateTime.Now.DayOfWeek.ToString() }
            };

            // Act
            var result = service.IsInSchedule();

            // Assert
            Assert.IsFalse(result, "Should return false when time format is invalid");
        }

        [TestMethod]
        public void ScheduleService_IsInSchedule_OutsideTimeWindow_ReturnsFalse()
        {
            // Arrange - set times that are definitely outside current time
            var now = DateTime.Now;
            var startHour = (now.Hour + 2) % 24; // 2 hours from now
            var stopHour = (now.Hour + 3) % 24;  // 3 hours from now
            
            var service = new ScheduleService
            {
                IsEnabled = true,
                StartTime = $"{startHour:D2}:00",
                StopTime = $"{stopHour:D2}:00",
                Days = new List<string> { now.DayOfWeek.ToString() }
            };

            // Act
            var result = service.IsInSchedule();

            // Assert
            Assert.IsFalse(result, "Should return false when current time is outside window");
        }

        [TestMethod]
        public void ScheduleService_DefaultValues_AreCorrect()
        {
            // Arrange
            var service = new ScheduleService();

            // Assert
            Assert.IsFalse(service.IsEnabled, "Should be disabled by default");
            Assert.AreEqual("09:00", service.StartTime, "Default start time should be 09:00");
            Assert.AreEqual("18:00", service.StopTime, "Default stop time should be 18:00");
            Assert.AreEqual(5, service.Days.Count, "Should have 5 default days (weekdays)");
            Assert.IsTrue(service.Days.Contains("Monday"), "Should include Monday");
            Assert.IsTrue(service.Days.Contains("Friday"), "Should include Friday");
        }

        [TestMethod]
        public void ScheduleService_Days_CanBeModified()
        {
            // Arrange
            var service = new ScheduleService();
            
            // Act
            service.Days = new List<string> { "Monday", "Wednesday", "Friday" };

            // Assert
            Assert.AreEqual(3, service.Days.Count, "Should have 3 days after modification");
            Assert.IsTrue(service.Days.Contains("Wednesday"), "Should include Wednesday");
        }

        [TestMethod]
        public void ScheduleService_StartTime_CanBeModified()
        {
            // Arrange
            var service = new ScheduleService();
            
            // Act
            service.StartTime = "08:30";

            // Assert
            Assert.AreEqual("08:30", service.StartTime, "Start time should be modified");
        }

        [TestMethod]
        public void ScheduleService_StopTime_CanBeModified()
        {
            // Arrange
            var service = new ScheduleService();
            
            // Act
            service.StopTime = "17:30";

            // Assert
            Assert.AreEqual("17:30", service.StopTime, "Stop time should be modified");
        }

        [TestMethod]
        public void ScheduleService_CheckAndUpdate_WhenDisabled_DoesNotThrow()
        {
            // Arrange
            var service = new ScheduleService();
            service.IsEnabled = false;
            var keepAwake = KeepAwakeService.Instance;
            var wasActive = keepAwake.IsActive;
            keepAwake.SetActive(false);

            try
            {
                // Act & Assert
                service.CheckAndUpdate(keepAwake);
                Assert.IsTrue(true, "CheckAndUpdate should not throw when disabled");
            }
            finally
            {
                keepAwake.SetActive(wasActive);
            }
        }

        [TestMethod]
        public void ScheduleService_CheckAndUpdate_WhenEnabled_DoesNotThrow()
        {
            // Arrange
            var service = new ScheduleService();
            service.IsEnabled = true;
            service.Days = new List<string> { DateTime.Now.DayOfWeek.ToString() };
            var keepAwake = KeepAwakeService.Instance;
            var wasActive = keepAwake.IsActive;
            keepAwake.SetActive(false);

            try
            {
                // Act & Assert
                service.CheckAndUpdate(keepAwake);
                Assert.IsTrue(true, "CheckAndUpdate should not throw when enabled");
            }
            finally
            {
                keepAwake.SetActive(wasActive);
            }
        }

        [TestMethod]
        public void ScheduleService_IsInSchedule_EmptyDays_ReturnsFalse()
        {
            // Arrange
            var service = new ScheduleService
            {
                IsEnabled = true,
                StartTime = "00:00",
                StopTime = "23:59",
                Days = new List<string>() // Empty days list
            };

            // Act
            var result = service.IsInSchedule();

            // Assert
            Assert.IsFalse(result, "Should return false when days list is empty");
        }
    }
}
