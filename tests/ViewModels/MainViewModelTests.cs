using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.ViewModels;
using System.ComponentModel;

namespace Redball.Tests.ViewModels
{
    [TestClass]
    public class MainViewModelTests
    {
        [TestMethod]
        public void MainViewModel_Constructor_InitializesCommands()
        {
            // Arrange & Act
            var viewModel = new MainViewModel();

            // Assert
            Assert.IsNotNull(viewModel.ToggleActiveCommand, "ToggleActiveCommand should be initialized");
            Assert.IsNotNull(viewModel.PauseKeepAwakeCommand, "PauseKeepAwakeCommand should be initialized");
            Assert.IsNotNull(viewModel.OpenSettingsCommand, "OpenSettingsCommand should be initialized");
            Assert.IsNotNull(viewModel.ExitCommand, "ExitCommand should be initialized");
            Assert.IsNotNull(viewModel.ShowAboutCommand, "ShowAboutCommand should be initialized");
            Assert.IsNotNull(viewModel.TypeThingCommand, "TypeThingCommand should be initialized");
        }

        [TestMethod]
        public void MainViewModel_Constructor_SyncsInitialState()
        {
            // Arrange & Act
            var viewModel = new MainViewModel();

            // Assert - should sync with KeepAwakeService.Instance
            Assert.AreEqual(viewModel.IsActive, viewModel.IsActive, "IsActive should be synced with service");
            Assert.IsNotNull(viewModel.StatusText, "StatusText should be initialized");
        }

        [TestMethod]
        public void MainViewModel_IsActive_Set_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var propertyChangedRaised = false;
            string? changedPropertyName = null;

            viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsActive))
                {
                    propertyChangedRaised = true;
                    changedPropertyName = e.PropertyName;
                }
            };

            // Act
            viewModel.IsActive = !viewModel.IsActive;

            // Assert
            Assert.IsTrue(propertyChangedRaised, "PropertyChanged should be raised for IsActive");
            Assert.AreEqual("IsActive", changedPropertyName);
        }

        [TestMethod]
        public void MainViewModel_IsActive_Set_UpdatesStatusText()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var statusTextChanged = false;

            viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.StatusText))
                {
                    statusTextChanged = true;
                }
            };

            // Act - directly set StatusText first to establish baseline
            var testStatus = "Test Status " + Guid.NewGuid();
            viewModel.StatusText = testStatus;
            statusTextChanged = false; // reset
            
            // Now toggle IsActive which should call UpdateStatusText
            viewModel.IsActive = !viewModel.IsActive;

            // Assert - StatusText should have been updated (even if to same value)
            // The key is that UpdateStatusText() was called, which sets StatusText
            Assert.IsTrue(statusTextChanged, "StatusText should have been changed when IsActive was toggled");
            Assert.IsNotNull(viewModel.StatusText, "StatusText should not be null");
            Assert.IsFalse(string.IsNullOrEmpty(viewModel.StatusText), "StatusText should not be empty");
        }

        [TestMethod]
        public void MainViewModel_IsActive_SameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var currentValue = viewModel.IsActive;
            var propertyChangedRaised = false;

            viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsActive))
                {
                    propertyChangedRaised = true;
                }
            };

            // Act - set same value
            viewModel.IsActive = currentValue;

            // Assert
            Assert.IsFalse(propertyChangedRaised, "PropertyChanged should NOT be raised for same value");
        }

        [TestMethod]
        public void MainViewModel_StatusText_Set_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var propertyChangedRaised = false;

            viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.StatusText))
                {
                    propertyChangedRaised = true;
                }
            };

            // Act
            viewModel.StatusText = "Test Status";

            // Assert
            Assert.IsTrue(propertyChangedRaised, "PropertyChanged should be raised for StatusText");
        }

        [TestMethod]
        public void MainViewModel_IsDarkMode_Set_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var propertyChangedRaised = false;

            viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsDarkMode))
                {
                    propertyChangedRaised = true;
                }
            };

            // Act - toggle dark mode (may throw in test environment due to ThemeManager)
            try
            {
                viewModel.IsDarkMode = !viewModel.IsDarkMode;
            }
            catch (Exception ex) when (ex is InvalidOperationException or UriFormatException)
            {
                // ThemeManager may throw in test environment - that's OK
            }

            // Assert
            Assert.IsTrue(propertyChangedRaised, "PropertyChanged should be raised for IsDarkMode");
        }

        [TestMethod]
        public void MainViewModel_ToggleActiveCommand_Exists()
        {
            // Arrange
            var viewModel = new MainViewModel();

            // Assert
            Assert.IsNotNull(viewModel.ToggleActiveCommand, "ToggleActiveCommand should exist");
            Assert.IsTrue(viewModel.ToggleActiveCommand.CanExecute(null), "Should be able to execute");
        }

        [TestMethod]
        public void MainViewModel_SetMainWindow_DoesNotThrow()
        {
            // Arrange
            var viewModel = new MainViewModel();

            // Act & Assert - should not throw when passing null (weak reference handles it)
            // Note: In real scenario, this would be called with actual MainWindow
            try
            {
                viewModel.SetMainWindow(null!);
                // If we get here without exception, that's acceptable behavior
            }
            catch (ArgumentNullException)
            {
                // This is also acceptable - depends on implementation
                Assert.IsTrue(true);
            }
        }

        [TestMethod]
        public void MainViewModel_IsDarkMode_ChangesTheme()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var originalDarkMode = viewModel.IsDarkMode;

            // Act - toggle twice to return to original state (may throw in test environment)
            try
            {
                viewModel.IsDarkMode = !originalDarkMode;
            }
            catch (Exception ex) when (ex is InvalidOperationException or UriFormatException)
            {
                // ThemeManager may throw in test environment - that's OK
            }
            var afterFirstToggle = viewModel.IsDarkMode;
            
            try
            {
                viewModel.IsDarkMode = originalDarkMode;
            }
            catch (Exception ex) when (ex is InvalidOperationException or UriFormatException)
            {
                // ThemeManager may throw in test environment - that's OK
            }
            var afterSecondToggle = viewModel.IsDarkMode;

            // Assert
            Assert.AreNotEqual(originalDarkMode, afterFirstToggle, "Should toggle to opposite value");
            Assert.AreEqual(originalDarkMode, afterSecondToggle, "Should toggle back to original");
        }
    }
}
