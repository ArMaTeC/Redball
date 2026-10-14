using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace Redball.Tests.ViewModels;

[TestClass]
public class CommandPaletteViewModelTests
{
    private static CommandPaletteViewModel CreateViewModel()
    {
        var commands = new List<PaletteCommand>
        {
            new() { Name = "Open Settings", Description = "Configure Redball", Icon = "⚙", Action = () => { } },
            new() { Name = "Start Keep-Awake", Description = "Begin an indefinite session", Icon = "☕", Action = () => { } },
            new() { Name = "Stop Session", Description = "End the current session", Icon = "⏹", Action = () => { } },
            new() { Name = "Toggle Theme", Description = "Switch between themes", Icon = "🎨", Action = () => { } },
            new() { Name = "View Logs", Description = "Open the log folder", Icon = "📄", Action = () => { } },
            new() { Name = "Exit", Description = "Quit Redball", Icon = "❌", Action = () => { } }
        };
        return new CommandPaletteViewModel(commands);
    }

    [TestMethod]
    public void Search_EmptyQuery_ShowsAllCommands()
    {
        var vm = CreateViewModel();
        vm.SearchText = "";

        Assert.IsTrue(vm.FilteredCommands.Count > 5);
    }

    [TestMethod]
    public void Search_ExactMatch_PrioritizesResult()
    {
        var vm = CreateViewModel();
        vm.SearchText = "Settings";

        var first = vm.FilteredCommands.FirstOrDefault();
        Assert.IsNotNull(first);
        StringAssert.Contains(first.Name, "Settings");
    }

    [TestMethod]
    public void Search_NameMatch_FindsResults()
    {
        var vm = CreateViewModel();
        vm.SearchText = "Keep-Awake";

        var match = vm.FilteredCommands.Any(c => c.Name.Contains("Start Keep-Awake"));
        Assert.IsTrue(match);
    }

    [TestMethod]
    public void Search_TimerIntent_GeneratesTimedSessionCommand()
    {
        var vm = CreateViewModel();
        // Intent regex requires a keyword before the duration: "(awake|for|min|stay) <n>"
        vm.SearchText = "stay awake for 20";

        var match = vm.FilteredCommands.Any(c => c.Name == "Start 20m Session");
        Assert.IsTrue(match);
    }

    [TestMethod]
    public void Search_ThemeIntent_GeneratesThemeCommands()
    {
        var vm = CreateViewModel();
        vm.SearchText = "dark theme";

        var match = vm.FilteredCommands.Any(c => c.Name == "Apply Dark Theme");
        Assert.IsTrue(match);
    }

    [TestMethod]
    public void ExecuteSelectedCommand_InvokesAction_AndHides()
    {
        var vm = CreateViewModel();
        bool executed = false;
        vm.SelectedCommand = new PaletteCommand { Name = "Test", Action = () => executed = true };
        vm.IsVisible = true;

        vm.ExecuteSelectedCommand.Execute(null);

        Assert.IsTrue(executed);
        Assert.IsFalse(vm.IsVisible);
        Assert.AreEqual("", vm.SearchText); // search is cleared after execution
    }
}
