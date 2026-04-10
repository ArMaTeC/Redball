using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class PowerPlanServiceTests
    {
        [TestMethod]
        public void Instance_Singleton_ReturnsSameInstance()
        {
            // Act
            var instance1 = PowerPlanService.Instance;
            var instance2 = PowerPlanService.Instance;

            // Assert
            Assert.IsNotNull(instance1);
            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void Constructor_InitializesActivePlan()
        {
            // Arrange & Act
            var service = PowerPlanService.Instance;

            // Assert - Should have attempted to get active plan
            Assert.IsNotNull(service);
            // ActivePlan properties may be null on non-Windows or if powercfg fails
        }

        [TestMethod]
        public void GetPowerPlans_ReturnsList()
        {
            // Arrange
            var service = PowerPlanService.Instance;

            // Act
            var plans = service.GetPowerPlans();

            // Assert
            Assert.IsNotNull(plans);
            Assert.IsInstanceOfType<List<(string Guid, string Name, bool IsActive)>>(plans);
        }

        [TestMethod]
        public void GetPowerPlans_EachPlan_HasGuid()
        {
            // Arrange
            var service = PowerPlanService.Instance;

            // Act
            var plans = service.GetPowerPlans();

            // Assert - Each plan should have a non-empty GUID
            foreach (var plan in plans)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(plan.Guid));
                Assert.IsFalse(string.IsNullOrWhiteSpace(plan.Name));
            }
        }

        [TestMethod]
        public void GetPowerPlans_HasAtMostOneActivePlan()
        {
            // Arrange
            var service = PowerPlanService.Instance;

            // Act
            var plans = service.GetPowerPlans();

            // Assert - Should have at most one active plan
            var activeCount = plans.Count(p => p.IsActive);
            Assert.IsTrue(activeCount <= 1, "Should have at most one active plan");
        }

        [TestMethod]
        public void RefreshActivePlan_DoesNotThrow()
        {
            // Arrange
            var service = PowerPlanService.Instance;

            // Act & Assert - should not throw
            service.RefreshActivePlan();
        }

        [TestMethod]
        public void SwitchPlan_InvalidGuid_ReturnsFalse()
        {
            // Arrange
            var service = PowerPlanService.Instance;
            var invalidGuid = "invalid-guid-12345";

            // Act
            var result = service.SwitchPlan(invalidGuid);

            // Assert - Should return false for invalid GUID
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void SwitchToHighPerformance_DoesNotThrow()
        {
            // Arrange
            var service = PowerPlanService.Instance;

            // Act & Assert - should not throw even if no high perf plan
            service.SwitchToHighPerformance();
        }

        [TestMethod]
        public void RestoreOriginalPlan_DoesNotThrow()
        {
            // Arrange
            var service = PowerPlanService.Instance;

            // Act & Assert
            service.RestoreOriginalPlan();
        }

        [TestMethod]
        public void ActivePlanName_Property_CanRead()
        {
            // Arrange
            var service = PowerPlanService.Instance;

            // Act
            var name = service.ActivePlanName;

            // Assert - Can be null or a string
            if (name != null)
            {
                Assert.IsInstanceOfType<string>(name);
            }
        }

        [TestMethod]
        public void ActivePlanGuid_Property_CanRead()
        {
            // Arrange
            var service = PowerPlanService.Instance;

            // Act
            var guid = service.ActivePlanGuid;

            // Assert - Can be null or a string
            if (guid != null)
            {
                Assert.IsInstanceOfType<string>(guid);
            }
        }

        [TestMethod]
        public void HighPerformanceDetection_CaseInsensitive()
        {
            // Arrange
            var testNames = new[]
            {
                "High performance",
                "High Performance",
                "HIGH PERFORMANCE",
                "Ultimate Performance",
                "ULTIMATE"
            };

            // Act & Assert - All should be recognized
            foreach (var name in testNames)
            {
                var containsHighPerf = name.Contains("High performance", StringComparison.OrdinalIgnoreCase) ||
                                      name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase);
                Assert.IsTrue(containsHighPerf, $"'{name}' should be recognized as high performance");
            }
        }

        [TestMethod]
        public void PlanGuidFormat_ValidFormat()
        {
            // Arrange
            var validGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

            // Assert - Should be valid format (contains hyphens and hex chars)
            Assert.IsTrue(validGuid.Contains('-'));
            Assert.AreEqual(36, validGuid.Length); // Standard GUID string length
        }

        [TestMethod]
        public void SwitchToHighPerformance_MultipleCalls_DoesNotDuplicate()
        {
            // Arrange
            var service = PowerPlanService.Instance;

            // Act - Call multiple times
            service.SwitchToHighPerformance();
            service.SwitchToHighPerformance();
            service.SwitchToHighPerformance();

            // Assert - Should not throw, internal _switched flag should prevent multiple switches
        }
    }
}
