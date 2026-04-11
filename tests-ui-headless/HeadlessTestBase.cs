using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Redball.UI.Services;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Redball.UI.Headless.Tests;

/// <summary>
/// Base class for headless UI tests that test ViewModel logic without WPF.
/// Provides mock services and helper methods for testing UI state transitions.
/// </summary>
public abstract class HeadlessTestBase
{
    protected Mock<KeepAwakeService>? MockKeepAwake { get; private set; }
    protected Mock<BatteryMonitorService>? MockBatteryMonitor { get; private set; }
    protected Mock<SessionStatsService>? MockSessionStats { get; private set; }
    protected Mock<ConfigService>? MockConfigService { get; private set; }

    [TestInitialize]
    public virtual void TestInitialize()
    {
        MockKeepAwake = new Mock<KeepAwakeService>();
        MockBatteryMonitor = new Mock<BatteryMonitorService>();
        MockSessionStats = new Mock<SessionStatsService>();
        MockConfigService = new Mock<ConfigService>();
    }

    [TestCleanup]
    public virtual void TestCleanup()
    {
        MockKeepAwake = null;
        MockBatteryMonitor = null;
        MockSessionStats = null;
        MockConfigService = null;
    }

    /// <summary>
    /// Waits for a property change notification on any INotifyPropertyChanged object.
    /// </summary>
    protected static async Task<bool> WaitForPropertyChangedAsync(
        INotifyPropertyChanged viewModel,
        string propertyName,
        TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource(timeout);

        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == propertyName)
            {
                tcs.TrySetResult(true);
            }
        }

        viewModel.PropertyChanged += Handler;

        try
        {
            using (cts.Token.Register(() => tcs.TrySetResult(false)))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            viewModel.PropertyChanged -= Handler;
        }
    }

    /// <summary>
    /// Asserts that a property changed event was raised.
    /// </summary>
    protected static void AssertPropertyChanged(
        INotifyPropertyChanged viewModel,
        string propertyName,
        Action action)
    {
        var propertyChanged = false;
        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == propertyName)
            {
                propertyChanged = true;
            }
        }

        viewModel.PropertyChanged += Handler;
        try
        {
            action();
            Assert.IsTrue(propertyChanged, $"Property '{propertyName}' should have raised PropertyChanged event.");
        }
        finally
        {
            viewModel.PropertyChanged -= Handler;
        }
    }
}
