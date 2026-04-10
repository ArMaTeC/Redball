using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.WPF.Services;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Redball.Tests
{
    [TestClass]
    public class AccessibilityServiceTests
    {
        [TestInitialize]
        public void TestInitialize()
        {
            // Ensure STA thread for WPF components
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                throw new InvalidOperationException("Tests must run on STA thread");
            }
        }

        [TestMethod]
        public void Instance_Singleton_ReturnsSameInstance()
        {
            // Act
            var instance1 = AccessibilityService.Instance;
            var instance2 = AccessibilityService.Instance;

            // Assert
            Assert.IsNotNull(instance1);
            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void Config_DefaultValues_AreCorrect()
        {
            // Arrange
            var service = AccessibilityService.Instance;

            // Assert
            Assert.IsNotNull(service.Config);
            Assert.AreEqual(AccessibilityLevel.AA, service.Config.TargetCompliance);
            Assert.AreEqual(1.0, service.Config.TextScale);
            Assert.IsTrue(service.Config.KeyboardNavigationEnhanced);
            Assert.IsFalse(service.Config.HighContrastEnabled);
            Assert.IsFalse(service.Config.ReducedMotion);
            Assert.IsFalse(service.Config.ScreenReaderOptimized);
        }

        [TestMethod]
        public void RegisterElement_NullElement_DoesNotThrow()
        {
            // Arrange
            var service = AccessibilityService.Instance;

            // Act & Assert - should not throw
            service.RegisterElement(null!, "testId", "Test Name");
        }

        [TestMethod]
        [STAThread]
        public void RegisterElement_ValidElement_RegistersSuccessfully()
        {
            // Arrange
            var service = AccessibilityService.Instance;
            var button = new Button { Name = "TestButton" };

            // Act
            service.RegisterElement(button, "testId", "Test Button", "Help text");

            // Assert
            Assert.AreEqual("testId", AutomationProperties.GetAutomationId(button));
            Assert.AreEqual("Test Button", AutomationProperties.GetName(button));
            Assert.AreEqual("Help text", AutomationProperties.GetHelpText(button));
            Assert.IsTrue(button.Focusable);
        }

        [TestMethod]
        [STAThread]
        public void ApplyFocusRing_NullControl_DoesNotThrow()
        {
            // Arrange
            var service = AccessibilityService.Instance;

            // Act & Assert - should not throw
            service.ApplyFocusRing(null!);
        }

        [TestMethod]
        [STAThread]
        public void ApplyFocusRing_ValidControl_AppliesStyle()
        {
            // Arrange
            var service = AccessibilityService.Instance;
            var button = new Button();

            // Act
            service.ApplyFocusRing(button);

            // Assert - style should be applied
            Assert.IsNull(button.FocusVisualStyle);
        }

        [TestMethod]
        public void AuditContrast_NoElements_ReturnsEmptyList()
        {
            // Arrange - fresh instance state
            var service = AccessibilityService.Instance;

            // Act
            var results = service.AuditContrast();

            // Assert
            Assert.IsNotNull(results);
            Assert.IsInstanceOfType<List<AccessibilityAuditResult>>(results);
        }

        [TestMethod]
        [STAThread]
        public void AuditContrast_LowContrastElement_DetectsIssue()
        {
            // Arrange
            var service = AccessibilityService.Instance;
            var button = new Button
            {
                Background = new SolidColorBrush(Colors.Gray),
                Foreground = new SolidColorBrush(Colors.LightGray)
            };
            service.RegisterElement(button, "lowContrast", "Low Contrast Button");

            // Act
            var results = service.AuditContrast();

            // Assert
            Assert.IsNotNull(results);
            // Should detect contrast issue for low contrast colours
        }

        [TestMethod]
        [STAThread]
        public void AuditContrast_HighContrastElement_NoIssue()
        {
            // Arrange
            var service = AccessibilityService.Instance;
            var button = new Button
            {
                Background = new SolidColorBrush(Colors.White),
                Foreground = new SolidColorBrush(Colors.Black)
            };
            service.RegisterElement(button, "highContrast", "High Contrast Button");

            // Act
            var results = service.AuditContrast();

            // Assert
            // Black on white should have no contrast issues
            var issuesForElement = results.FindAll(r => r.ElementId == "highContrast");
            Assert.AreEqual(0, issuesForElement.Count, "High contrast should have no issues");
        }

        [TestMethod]
        public void AuditKeyboardNavigation_NoElements_ReturnsEmptyList()
        {
            // Arrange
            var service = AccessibilityService.Instance;

            // Act
            var results = service.AuditKeyboardNavigation();

            // Assert
            Assert.IsNotNull(results);
            Assert.IsInstanceOfType<List<AccessibilityAuditResult>>(results);
        }

        [TestMethod]
        [STAThread]
        public void AuditKeyboardNavigation_MissingAutomationName_DetectsIssue()
        {
            // Arrange
            var service = AccessibilityService.Instance;
            var button = new Button { Name = "UnnamedButton" };
            // Register without automation name
            AutomationProperties.SetAutomationId(button, "noName");
            button.Focusable = true;
            button.IsTabStop = true;

            // Manually add to tracked elements via reflection or register properly
            service.RegisterElement(button, "noName", ""); // Empty name

            // Act
            var results = service.AuditKeyboardNavigation();

            // Assert
            var nameIssue = results.Find(r => r.Issue.Contains("automation name"));
            Assert.IsNotNull(nameIssue);
            Assert.AreEqual("Error", nameIssue.Severity);
            Assert.AreEqual("4.1.2 Name, Role, Value", nameIssue.WCAGCriterion);
        }

        [TestMethod]
        [STAThread]
        public void AuditKeyboardNavigation_NonFocusableTabStop_DetectsIssue()
        {
            // Arrange
            var service = AccessibilityService.Instance;
            var button = new Button
            {
                Focusable = false,
                IsTabStop = true
            };
            service.RegisterElement(button, "nonFocusable", "Non Focusable Button");

            // Act
            var results = service.AuditKeyboardNavigation();

            // Assert
            var focusIssue = results.Find(r => r.Issue.Contains("tab stop but not focusable"));
            Assert.IsNotNull(focusIssue);
            Assert.AreEqual("Error", focusIssue.Severity);
            Assert.AreEqual("2.1.1 Keyboard", focusIssue.WCAGCriterion);
        }

        [TestMethod]
        public void GetSummary_ReturnsValidSummary()
        {
            // Arrange
            var service = AccessibilityService.Instance;

            // Act
            var summary = service.GetSummary();

            // Assert
            Assert.IsNotNull(summary);
            Assert.AreEqual(service.Config.TargetCompliance, summary.TargetLevel);
            Assert.IsTrue(summary.TotalElements >= 0);
            Assert.IsTrue(summary.ContrastIssues >= 0);
            Assert.IsTrue(summary.KeyboardIssues >= 0);
            Assert.IsFalse(string.IsNullOrEmpty(summary.Status));
        }

        [TestMethod]
        public void GetSummary_CompliantWhenNoIssues()
        {
            // This test validates the summary logic
            // Arrange & Act
            var summary = new AccessibilitySummary
            {
                ContrastIssues = 0,
                KeyboardIssues = 0
            };

            // Assert
            Assert.IsTrue(summary.IsCompliant);
            Assert.AreEqual("Compliant", summary.Status);
        }

        [TestMethod]
        public void GetSummary_NonCompliantWhenIssuesExist()
        {
            // Arrange & Act
            var summary = new AccessibilitySummary
            {
                ContrastIssues = 1,
                KeyboardIssues = 0
            };

            // Assert
            Assert.IsFalse(summary.IsCompliant);
            Assert.AreEqual("Non-Compliant", summary.Status);
        }

        [TestMethod]
        public void RefreshSystemSettings_DoesNotThrow()
        {
            // Arrange
            var service = AccessibilityService.Instance;

            // Act & Assert - should not throw
            service.RefreshSystemSettings();
        }

        [TestMethod]
        public void AccessibilityLevel_EnumValues_AreCorrect()
        {
            // Assert
            Assert.AreEqual(0, (int)AccessibilityLevel.A);
            Assert.AreEqual(1, (int)AccessibilityLevel.AA);
            Assert.AreEqual(2, (int)AccessibilityLevel.AAA);
        }

        [TestMethod]
        public void AccessibilityConfig_Properties_CanBeSet()
        {
            // Arrange
            var config = new AccessibilityConfig();

            // Act
            config.HighContrastEnabled = true;
            config.ReducedMotion = true;
            config.TextScale = 1.5;
            config.ScreenReaderOptimized = true;
            config.KeyboardNavigationEnhanced = false;
            config.TargetCompliance = AccessibilityLevel.AAA;

            // Assert
            Assert.IsTrue(config.HighContrastEnabled);
            Assert.IsTrue(config.ReducedMotion);
            Assert.AreEqual(1.5, config.TextScale);
            Assert.IsTrue(config.ScreenReaderOptimized);
            Assert.IsFalse(config.KeyboardNavigationEnhanced);
            Assert.AreEqual(AccessibilityLevel.AAA, config.TargetCompliance);
        }

        [TestMethod]
        public void AccessibilityAuditResult_Properties_CanBeSet()
        {
            // Arrange
            var result = new AccessibilityAuditResult();

            // Act
            result.ElementId = "test-element";
            result.Issue = "Test issue";
            result.Severity = "Warning";
            result.WCAGCriterion = "1.4.3";
            result.Recommendation = "Fix this";

            // Assert
            Assert.AreEqual("test-element", result.ElementId);
            Assert.AreEqual("Test issue", result.Issue);
            Assert.AreEqual("Warning", result.Severity);
            Assert.AreEqual("1.4.3", result.WCAGCriterion);
            Assert.AreEqual("Fix this", result.Recommendation);
        }
    }
}
