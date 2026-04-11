using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Redball.UI.Services;
using Redball.UI.ViewModels;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Media;

namespace Redball.UI.Headless.Tests;

/// <summary>
/// Headless UI tests for MainViewModel.
/// Tests ViewModel logic without requiring WPF UI automation.
/// </summary>
[TestClass]
[ExcludeFromCodeCoverage]
public class MainViewModelHeadlessTests : HeadlessTestBase
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
    [TestCategory("UI"), TestCategory("Smoke"), TestCategory("Headless")]
    public void ViewModel_InitialisesWithExpectedDefaults()
    {
        // Assert
        Assert.IsNotNull(_viewModel, "ViewModel should be created");
        Assert.IsTrue(_viewModel.IsActive, "Should be active by default");
        Assert.IsNotNull(_viewModel.StatusText, "Status text should be set");
        Assert.IsTrue(_viewModel.StatusText.Contains("Active"), "Status should indicate active state");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void ViewModel_HasStatusText()
    {
        // Arrange & Act
        var status = _viewModel!.StatusText;

        // Assert
        Assert.IsFalse(string.IsNullOrEmpty(status), "Status text should not be empty");
        Assert.IsTrue(status.Contains("Active") || status.Contains("Inactive"),
            $"Status should indicate active/inactive state, but was: {status}");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void ToggleCommand_CanBeExecuted()
    {
        // Assert
        Assert.IsTrue(_viewModel!.ToggleActiveCommand.CanExecute(null),
            "Toggle command should be executable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void ToggleCommand_Execution_TogglesActiveState()
    {
        // Arrange
        var initialState = _viewModel!.IsActive;

        // Act
        _viewModel.ToggleActiveCommand.Execute(null);

        // Assert
        Assert.AreNotEqual(initialState, _viewModel.IsActive,
            "Active state should toggle after command execution");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void ToggleCommand_RaisesPropertyChanged()
    {
        // Assert
        AssertPropertyChanged(_viewModel!, nameof(MainViewModel.IsActive), () =>
        {
            _viewModel!.ToggleActiveCommand.Execute(null);
        });
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void PreventDisplaySleep_CanBeToggled()
    {
        // Arrange
        var initialValue = _viewModel!.PreventDisplaySleep;

        // Act
        _viewModel.PreventDisplaySleep = !initialValue;

        // Assert
        Assert.AreNotEqual(initialValue, _viewModel.PreventDisplaySleep,
            "PreventDisplaySleep should be togglable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void PreventDisplaySleep_RaisesPropertyChanged()
    {
        // Assert
        AssertPropertyChanged(_viewModel!, nameof(MainViewModel.PreventDisplaySleep), () =>
        {
            _viewModel!.PreventDisplaySleep = !_viewModel.PreventDisplaySleep;
        });
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void UseHeartbeat_CanBeToggled()
    {
        // Arrange
        var initialValue = _viewModel!.UseHeartbeat;

        // Act
        _viewModel.UseHeartbeat = !initialValue;

        // Assert
        Assert.AreNotEqual(initialValue, _viewModel.UseHeartbeat,
            "UseHeartbeat should be togglable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void UseHeartbeat_RaisesPropertyChanged()
    {
        // Assert
        AssertPropertyChanged(_viewModel!, nameof(MainViewModel.UseHeartbeat), () =>
        {
            _viewModel!.UseHeartbeat = !_viewModel.UseHeartbeat;
        });
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void DriverSelection_CanBeChanged()
    {
        // Arrange
        var initial = _viewModel!.TypeThingDriverSelection;
        var options = _viewModel.DriverSelectionOptions.ToList();

        // Assert options exist
        Assert.IsTrue(options.Count > 0, "Should have driver selection options");

        // Act - select different option if available
        var newOption = options.FirstOrDefault(o => o != initial);
        if (newOption != default)
        {
            _viewModel.TypeThingDriverSelection = newOption;

            // Assert
            Assert.AreEqual(newOption, _viewModel.TypeThingDriverSelection,
                "Driver selection should be changeable");
        }
    }


    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void IsDarkMode_CanBeToggled()
    {
        // Arrange
        var initialValue = _viewModel!.IsDarkMode;

        // Act
        _viewModel.IsDarkMode = !initialValue;

        // Assert
        Assert.AreNotEqual(initialValue, _viewModel.IsDarkMode,
            "Dark mode should be togglable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void IsDarkMode_RaisesPropertyChanged()
    {
        // Assert
        AssertPropertyChanged(_viewModel!, nameof(MainViewModel.IsDarkMode), () =>
        {
            _viewModel!.IsDarkMode = !_viewModel.IsDarkMode;
        });
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void TypeThingSessionsToday_HasValidValue()
    {
        // Assert - should have a non-negative value
        Assert.IsTrue(_viewModel!.TypeThingSessionsToday >= 0,
            "TypeThing sessions today should be non-negative");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void CharsTypedToday_HasValidValue()
    {
        // Assert - should have a non-negative value
        Assert.IsTrue(_viewModel!.CharsTypedToday >= 0,
            "Chars typed today should be non-negative");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void KeepAwakeSessionsToday_HasValidValue()
    {
        // Assert - should have a non-negative value
        Assert.IsTrue(_viewModel!.KeepAwakeSessionsToday >= 0,
            "Keep-awake sessions today should be non-negative");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void ChartPoints_AreInitialised()
    {
        // Assert
        Assert.IsNotNull(_viewModel!.TypeThingChartPoints,
            "TypeThing chart points should be initialised");
        Assert.IsNotNull(_viewModel.ActivityChartPoints,
            "Activity chart points should be initialised");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void StatusText_UpdatesWhenStateChanges()
    {
        // Arrange
        var initialStatus = _viewModel!.StatusText;

        // Act - toggle active state
        _viewModel.ToggleActiveCommand.Execute(null);

        // Assert - status text should change
        Assert.AreNotEqual(initialStatus, _viewModel.StatusText,
            "Status text should update when state changes");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Regression"), TestCategory("Headless")]
    public void ToggleKeepAwake_Twice_ReturnsToOriginalState()
    {
        // Arrange
        var initialState = _viewModel!.IsActive;

        // Act
        _viewModel.ToggleActiveCommand.Execute(null);
        _viewModel.ToggleActiveCommand.Execute(null);

        // Assert
        Assert.AreEqual(initialState, _viewModel.IsActive,
            "Toggling twice should return to original state");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Performance"), TestCategory("Headless")]
    public void PropertySetters_RespondWithinAcceptableTime()
    {
        // Arrange
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act - set multiple properties
        _viewModel!.PreventDisplaySleep = !_viewModel.PreventDisplaySleep;
        _viewModel.UseHeartbeat = !_viewModel.UseHeartbeat;
        _viewModel.IsDarkMode = !_viewModel.IsDarkMode;

        stopwatch.Stop();

        // Assert - property sets should be fast
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 100,
            $"Property sets took {stopwatch.ElapsedMilliseconds}ms, exceeding 100ms threshold");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Accessibility"), TestCategory("Headless")]
    public void ViewModel_ImplementsINotifyPropertyChanged()
    {
        // Assert
        Assert.IsInstanceOfType(_viewModel, typeof(INotifyPropertyChanged),
            "ViewModel should implement INotifyPropertyChanged for data binding");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Headless")]
    public void Commands_AreNotNull()
    {
        // Assert - all commands should be initialised
        Assert.IsNotNull(_viewModel!.ToggleActiveCommand, "ToggleActiveCommand should not be null");
        Assert.IsNotNull(_viewModel.OpenSettingsCommand, "OpenSettingsCommand should not be null");
        Assert.IsNotNull(_viewModel.ExitCommand, "ExitCommand should not be null");
        Assert.IsNotNull(_viewModel.ToggleDisplaySleepCommand, "ToggleDisplaySleepCommand should not be null");
        Assert.IsNotNull(_viewModel.ToggleHeartbeatCommand, "ToggleHeartbeatCommand should not be null");
        Assert.IsNotNull(_viewModel.OpenTypeThingCommand, "OpenTypeThingCommand should not be null");
        Assert.IsNotNull(_viewModel.OpenAnalyticsCommand, "OpenAnalyticsCommand should not be null");
        Assert.IsNotNull(_viewModel.OpenDiagnosticsCommand, "OpenDiagnosticsCommand should not be null");
        Assert.IsNotNull(_viewModel.OpenBehaviorCommand, "OpenBehaviorCommand should not be null");
        Assert.IsNotNull(_viewModel.OpenSmartFeaturesCommand, "OpenSmartFeaturesCommand should not be null");
        Assert.IsNotNull(_viewModel.OpenUpdatesCommand, "OpenUpdatesCommand should not be null");
    }
}
