using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.ViewModels;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Media;

namespace Redball.UI.Headless.Tests;

/// <summary>
/// Headless tests for dashboard graph data binding.
/// Validates the real data wiring fix (replacing static/mock data).
/// </summary>
[TestClass]
[ExcludeFromCodeCoverage]
public class GraphDataHeadlessTests : HeadlessTestBase
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
    [TestCategory("UI"), TestCategory("Graphs"), TestCategory("Headless")]
    public void Graph_TypeThingChartPoints_Initialised()
    {
        // Assert
        Assert.IsNotNull(_viewModel!.TypeThingChartPoints,
            "TypeThing chart points should be initialised");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Graphs"), TestCategory("Headless")]
    public void Graph_ActivityChartPoints_Initialised()
    {
        // Assert
        Assert.IsNotNull(_viewModel!.ActivityChartPoints,
            "Activity chart points should be initialised");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Graphs"), TestCategory("Headless")]
    public void Graph_TypeThingChartPoints_NotEmpty()
    {
        // Assert - should have data points (not empty mock data)
        Assert.IsTrue(_viewModel!.TypeThingChartPoints.Count > 0,
            "TypeThing chart should have data points from real data source");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Graphs"), TestCategory("Headless")]
    public void Graph_ActivityChartPoints_NotEmpty()
    {
        // Assert - should have data points
        Assert.IsTrue(_viewModel!.ActivityChartPoints.Count > 0,
            "Activity chart should have data points from real data source");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Graphs"), TestCategory("Headless")]
    public void Graph_ChartPoints_HaveValidCoordinates()
    {
        // Assert - all points should have valid (non-negative) coordinates
        foreach (var point in _viewModel!.TypeThingChartPoints)
        {
            Assert.IsTrue(point.X >= 0, "TypeThing chart X coordinate should be non-negative");
            Assert.IsTrue(point.Y >= 0, "TypeThing chart Y coordinate should be non-negative");
        }

        foreach (var point in _viewModel.ActivityChartPoints)
        {
            Assert.IsTrue(point.X >= 0, "Activity chart X coordinate should be non-negative");
            Assert.IsTrue(point.Y >= 0, "Activity chart Y coordinate should be non-negative");
        }
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Graphs"), TestCategory("Headless")]
    public void Graph_TypeThingSessions_HasValidValue()
    {
        // Assert
        Assert.IsTrue(_viewModel!.TypeThingSessionsToday >= 0,
            "TypeThing sessions should be non-negative");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Graphs"), TestCategory("Headless")]
    public void Graph_CharsTyped_HasValidValue()
    {
        // Assert
        Assert.IsTrue(_viewModel!.CharsTypedToday >= 0,
            "Chars typed should be non-negative");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Graphs"), TestCategory("Headless")]
    public void Graph_KeepAwakeSessions_HasValidValue()
    {
        // Assert
        Assert.IsTrue(_viewModel!.KeepAwakeSessionsToday >= 0,
            "Keep-awake sessions should be non-negative");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Graphs"), TestCategory("Headless")]
    public void Graph_StatsAreConsistent()
    {
        // Assert - stats should be internally consistent
        // Sessions today should not exceed reasonable bounds
        Assert.IsTrue(_viewModel!.TypeThingSessionsToday < 10000,
            "TypeThing sessions should be within reasonable bounds");
        Assert.IsTrue(_viewModel.CharsTypedToday < 1000000,
            "Chars typed should be within reasonable bounds");
        Assert.IsTrue(_viewModel.KeepAwakeSessionsToday < 10000,
            "Keep-awake sessions should be within reasonable bounds");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Graphs"), TestCategory("Headless")]
    public void Graph_DisplayStrings_AreNotEmpty()
    {
        // Assert - formatted display strings should not be empty
        Assert.IsFalse(string.IsNullOrEmpty(_viewModel!.TypeThingSessionsTodayText),
            "TypeThing sessions display text should not be empty");
        Assert.IsFalse(string.IsNullOrEmpty(_viewModel.CharsTypedTodayText),
            "Chars typed display text should not be empty");
        Assert.IsFalse(string.IsNullOrEmpty(_viewModel.KeepAwakeSessionsTodayText),
            "Keep-awake sessions display text should not be empty");
        Assert.IsFalse(string.IsNullOrEmpty(_viewModel.KeepAwakeTimeTodayText),
            "Keep-awake time display text should not be empty");
        Assert.IsFalse(string.IsNullOrEmpty(_viewModel.AvgCharsPerMinuteText),
            "Avg chars per minute display text should not be empty");
    }

    [TestMethod]
    [TestCategory("UI"), TestCategory("Graphs"), TestCategory("Headless")]
    public void Graph_ChartPoints_AreValidAfterInitialisation()
    {
        // Arrange & Act - ViewModel is already initialised

        // Assert - chart points should have valid structure
        Assert.IsNotNull(_viewModel!.TypeThingChartPoints, "TypeThingChartPoints should not be null");
        Assert.IsNotNull(_viewModel.ActivityChartPoints, "ActivityChartPoints should not be null");

        // Points should be within expected bounds if they exist
        foreach (var point in _viewModel.TypeThingChartPoints)
        {
            Assert.IsTrue(point.X >= 0, "Chart X should be non-negative");
            Assert.IsTrue(point.Y >= 0, "Chart Y should be non-negative");
        }
    }
}
