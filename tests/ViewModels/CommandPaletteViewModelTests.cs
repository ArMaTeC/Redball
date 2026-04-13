using Xunit;
using Redball.UI.ViewModels;
using System.Linq;
using System.Threading.Tasks;

namespace Redball.Tests.ViewModels;

public class CommandPaletteViewModelTests
{
    [Fact]
    public void Search_EmptyQuery_ShowsAllCommands()
    {
        var vm = new CommandPaletteViewModel();
        vm.SearchQuery = "";
        
        Assert.NotEmpty(vm.FilteredCommands);
        Assert.True(vm.FilteredCommands.Count > 5);
    }

    [Fact]
    public void Search_ExactMatch_PrioritizesResult()
    {
        var vm = new CommandPaletteViewModel();
        vm.SearchQuery = "Settings";
        
        var first = vm.FilteredCommands.FirstOrDefault();
        Assert.NotNull(first);
        Assert.Contains("Settings", first.DisplayName);
    }

    [Fact]
    public void Search_FuzzyMatch_FindsResults()
    {
        var vm = new CommandPaletteViewModel();
        vm.SearchQuery = "stngs"; // Fuzzy for Settings
        
        var match = vm.FilteredCommands.Any(c => c.DisplayName.Contains("Settings"));
        Assert.True(match);
    }

    [Fact]
    public void Search_TimedSession_IntentDetected()
    {
        var vm = new CommandPaletteViewModel();
        vm.SearchQuery = "stay awake for 20m";
        
        var first = vm.FilteredCommands.FirstOrDefault();
        Assert.NotNull(first);
        Assert.Contains("Start 20m Session", first.DisplayName);
    }

    [Fact]
    public void Search_ShortIntent_Detected()
    {
        var vm = new CommandPaletteViewModel();
        vm.SearchQuery = "20m";
        
        var match = vm.FilteredCommands.Any(c => c.DisplayName.Contains("Start 20m Session"));
        Assert.True(match);
    }

    [Fact]
    public void ExecuteCommand_InvokesAction()
    {
        var vm = new CommandPaletteViewModel();
        bool executed = false;
        var cmd = new CommandItem("Test", "t", () => executed = true);
        
        vm.ExecuteCommand(cmd);
        
        Assert.True(executed);
        Assert.False(vm.IsVisible); // Should hide after execution
    }
}
