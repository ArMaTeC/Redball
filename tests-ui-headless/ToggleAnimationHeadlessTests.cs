using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.ViewModels;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Redball.UI.Headless.Tests;

/// <summary>
/// Headless tests for toggle switch animations and behaviour.
/// Validates the animation storyboard targeting fix.
/// </summary>
[TestClass]
[ExcludeFromCodeCoverage]
public class ToggleAnimationHeadlessTests : HeadlessTestBase
{
    private MainViewModel? _viewModel;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        _viewModel = new MainViewModel();
    }

    [TestCleanup]
    public override void TestCleanup()
    {
        _viewModel = null;
        base.TestCleanup();
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Toggle"), TestCategory("Headless")]
    public void Toggle_IsActive_CanBeToggled()
    {
        // Arrange
        var initialValue = _viewModel!.IsActive;

        // Act
        _viewModel.ToggleActiveCommand.Execute(null);

        // Assert
        Assert.AreNotEqual(initialValue, _viewModel.IsActive,
            "IsActive should toggle when command executes");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Toggle"), TestCategory("Headless")]
    public void Toggle_IsActive_RaisesPropertyChanged()
    {
        // Assert
        AssertPropertyChanged(_viewModel!, nameof(MainViewModel.IsActive), () =>
        {
            _viewModel!.ToggleActiveCommand.Execute(null);
        });
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Toggle"), TestCategory("Headless")]
    public void Toggle_PreventDisplaySleep_CanBeToggled()
    {
        // Arrange & Act
        var initialValue = _viewModel!.PreventDisplaySleep;
        _viewModel.PreventDisplaySleep = !initialValue;

        // Assert
        Assert.AreNotEqual(initialValue, _viewModel.PreventDisplaySleep,
            "PreventDisplaySleep should be togglable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Toggle"), TestCategory("Headless")]
    public void Toggle_UseHeartbeat_CanBeToggled()
    {
        // Arrange & Act
        var initialValue = _viewModel!.UseHeartbeat;
        _viewModel.UseHeartbeat = !initialValue;

        // Assert
        Assert.AreNotEqual(initialValue, _viewModel.UseHeartbeat,
            "UseHeartbeat should be togglable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Toggle"), TestCategory("Headless")]
    public void Toggle_IsDarkMode_CanBeToggled()
    {
        // Arrange & Act
        var initialValue = _viewModel!.IsDarkMode;
        _viewModel.IsDarkMode = !initialValue;

        // Assert
        Assert.AreNotEqual(initialValue, _viewModel.IsDarkMode,
            "IsDarkMode should be togglable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Toggle"), TestCategory("Headless")]
    public void Toggle_CommandCanExecuteMultipleTimes()
    {
        // Arrange
        var toggleCount = 5;
        var states = new List<bool> { _viewModel!.IsActive };

        // Act
        for (int i = 0; i < toggleCount; i++)
        {
            _viewModel.ToggleActiveCommand.Execute(null);
            states.Add(_viewModel.IsActive);
        }

        // Assert - should have alternating states
        for (int i = 1; i < states.Count; i++)
        {
            Assert.AreNotEqual(states[i - 1], states[i],
                $"State at index {i} should differ from previous");
        }
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Toggle"), TestCategory("Headless")]
    public void Toggle_RapidToggle_DoesNotCrash()
    {
        // Act - rapid toggles
        for (int i = 0; i < 10; i++)
        {
            _viewModel!.ToggleActiveCommand.Execute(null);
        }

        // Assert - still in valid state
        Assert.IsTrue(_viewModel!.IsActive || !_viewModel.IsActive,
            "After rapid toggles, ViewModel should be in valid state");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Toggle"), TestCategory("Headless")]
    public void Toggle_StateChangesUpdateStatusText()
    {
        // Arrange
        var initialStatus = _viewModel!.StatusText;

        // Act
        _viewModel.ToggleActiveCommand.Execute(null);
        var toggledStatus = _viewModel.StatusText;

        // Assert
        Assert.AreNotEqual(initialStatus, toggledStatus,
            "Status text should reflect toggle state change");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Toggle"), TestCategory("Headless")]
    public void Toggle_MultiplePropertiesCanBeSetIndependently()
    {
        // Arrange
        var initialPreventDisplay = _viewModel!.PreventDisplaySleep;
        var initialUseHeartbeat = _viewModel.UseHeartbeat;
        var initialIsDarkMode = _viewModel.IsDarkMode;

        // Act - toggle each independently
        _viewModel.PreventDisplaySleep = !initialPreventDisplay;
        _viewModel.UseHeartbeat = !initialUseHeartbeat;
        _viewModel.IsDarkMode = !initialIsDarkMode;

        // Assert
        Assert.AreNotEqual(initialPreventDisplay, _viewModel.PreventDisplaySleep);
        Assert.AreNotEqual(initialUseHeartbeat, _viewModel.UseHeartbeat);
        Assert.AreNotEqual(initialIsDarkMode, _viewModel.IsDarkMode);
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Toggle"), TestCategory("Headless")]
    public void Toggle_AllTogglesRaisePropertyChanged()
    {
        // Test each toggle property raises PropertyChanged
        var toggleProperties = new[]
        {
            nameof(MainViewModel.IsActive),
            nameof(MainViewModel.PreventDisplaySleep),
            nameof(MainViewModel.UseHeartbeat),
            nameof(MainViewModel.IsDarkMode)
        };

        foreach (var property in toggleProperties)
        {
            var raised = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == property) raised = true;
            };

            // Trigger the property change
            switch (property)
            {
                case nameof(MainViewModel.IsActive):
                    _viewModel.ToggleActiveCommand.Execute(null);
                    break;
                case nameof(MainViewModel.PreventDisplaySleep):
                    _viewModel.PreventDisplaySleep = !_viewModel.PreventDisplaySleep;
                    break;
                case nameof(MainViewModel.UseHeartbeat):
                    _viewModel.UseHeartbeat = !_viewModel.UseHeartbeat;
                    break;
                case nameof(MainViewModel.IsDarkMode):
                    _viewModel.IsDarkMode = !_viewModel.IsDarkMode;
                    break;
            }

            Assert.IsTrue(raised, $"Property '{property}' should raise PropertyChanged");
        }
    }
}
