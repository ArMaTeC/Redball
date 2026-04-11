using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.ViewModels;
using System.Diagnostics.CodeAnalysis;

namespace Redball.UI.Headless.Tests;

/// <summary>
/// Headless tests for navigation commands.
/// Tests that navigation commands are available and executable.
/// </summary>
[TestClass]
[ExcludeFromCodeCoverage]
public class NavigationHeadlessTests : HeadlessTestBase
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
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenSettingsCommand_Exists()
    {
        // Assert
        Assert.IsNotNull(_viewModel!.OpenSettingsCommand, "OpenSettingsCommand should exist");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenSettingsCommand_CanExecute()
    {
        // Assert
        Assert.IsTrue(_viewModel!.OpenSettingsCommand.CanExecute(null),
            "OpenSettingsCommand should be executable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenTypeThingCommand_Exists()
    {
        // Assert
        Assert.IsNotNull(_viewModel!.OpenTypeThingCommand, "OpenTypeThingCommand should exist");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenTypeThingCommand_CanExecute()
    {
        // Assert
        Assert.IsTrue(_viewModel!.OpenTypeThingCommand.CanExecute(null),
            "OpenTypeThingCommand should be executable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenAnalyticsCommand_Exists()
    {
        // Assert
        Assert.IsNotNull(_viewModel!.OpenAnalyticsCommand, "OpenAnalyticsCommand should exist");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenAnalyticsCommand_CanExecute()
    {
        // Assert
        Assert.IsTrue(_viewModel!.OpenAnalyticsCommand.CanExecute(null),
            "OpenAnalyticsCommand should be executable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenDiagnosticsCommand_Exists()
    {
        // Assert
        Assert.IsNotNull(_viewModel!.OpenDiagnosticsCommand, "OpenDiagnosticsCommand should exist");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenDiagnosticsCommand_CanExecute()
    {
        // Assert
        Assert.IsTrue(_viewModel!.OpenDiagnosticsCommand.CanExecute(null),
            "OpenDiagnosticsCommand should be executable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenBehaviorCommand_Exists()
    {
        // Assert
        Assert.IsNotNull(_viewModel!.OpenBehaviorCommand, "OpenBehaviorCommand should exist");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenBehaviorCommand_CanExecute()
    {
        // Assert
        Assert.IsTrue(_viewModel!.OpenBehaviorCommand.CanExecute(null),
            "OpenBehaviorCommand should be executable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenSmartFeaturesCommand_Exists()
    {
        // Assert
        Assert.IsNotNull(_viewModel!.OpenSmartFeaturesCommand, "OpenSmartFeaturesCommand should exist");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenSmartFeaturesCommand_CanExecute()
    {
        // Assert
        Assert.IsTrue(_viewModel!.OpenSmartFeaturesCommand.CanExecute(null),
            "OpenSmartFeaturesCommand should be executable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenUpdatesCommand_Exists()
    {
        // Assert
        Assert.IsNotNull(_viewModel!.OpenUpdatesCommand, "OpenUpdatesCommand should exist");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenUpdatesCommand_CanExecute()
    {
        // Assert
        Assert.IsTrue(_viewModel!.OpenUpdatesCommand.CanExecute(null),
            "OpenUpdatesCommand should be executable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenLogsCommand_Exists()
    {
        // Assert
        Assert.IsNotNull(_viewModel!.OpenLogsCommand, "OpenLogsCommand should exist");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenLogsCommand_CanExecute()
    {
        // Assert
        Assert.IsTrue(_viewModel!.OpenLogsCommand.CanExecute(null),
            "OpenLogsCommand should be executable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenMetricsCommand_Exists()
    {
        // Assert
        Assert.IsNotNull(_viewModel!.OpenMetricsCommand, "OpenMetricsCommand should exist");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_OpenMetricsCommand_CanExecute()
    {
        // Assert
        Assert.IsTrue(_viewModel!.OpenMetricsCommand.CanExecute(null),
            "OpenMetricsCommand should be executable");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Navigation"), TestCategory("Headless")]
    public void Navigation_AllNavigationCommands_AreAvailable()
    {
        // Assert - all navigation commands should be initialised
        Assert.IsNotNull(_viewModel!.OpenSettingsCommand, "OpenSettingsCommand should exist");
        Assert.IsNotNull(_viewModel.OpenTypeThingCommand, "OpenTypeThingCommand should exist");
        Assert.IsNotNull(_viewModel.OpenAnalyticsCommand, "OpenAnalyticsCommand should exist");
        Assert.IsNotNull(_viewModel.OpenDiagnosticsCommand, "OpenDiagnosticsCommand should exist");
        Assert.IsNotNull(_viewModel.OpenBehaviorCommand, "OpenBehaviorCommand should exist");
        Assert.IsNotNull(_viewModel.OpenSmartFeaturesCommand, "OpenSmartFeaturesCommand should exist");
        Assert.IsNotNull(_viewModel.OpenUpdatesCommand, "OpenUpdatesCommand should exist");
        Assert.IsNotNull(_viewModel.OpenLogsCommand, "OpenLogsCommand should exist");
        Assert.IsNotNull(_viewModel.OpenMetricsCommand, "OpenMetricsCommand should exist");
    }
}
