using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Redball.UI.ViewModels;

public class CommandPaletteViewModel : ViewModelBase
{
    private string _searchText = "";
    private bool _isVisible;
    private PaletteCommand? _selectedCommand;
    private readonly IEnumerable<PaletteCommand> _allCommands;

    public CommandPaletteViewModel(IEnumerable<PaletteCommand> commands)
    {
        _allCommands = commands;
        FilteredCommands = new ObservableCollection<PaletteCommand>(_allCommands);
        
        ExecuteSelectedCommand = new RelayCommand(ExecuteSelected);
        CloseCommand = new RelayCommand(() => IsVisible = false);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                FilterCommands();
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public PaletteCommand? SelectedCommand
    {
        get => _selectedCommand;
        set => SetProperty(ref _selectedCommand, value);
    }

    public ObservableCollection<PaletteCommand> FilteredCommands { get; }

    public ICommand ExecuteSelectedCommand { get; }
    public ICommand CloseCommand { get; }

    private void FilterCommands()
    {
        FilteredCommands.Clear();
        var matches = string.IsNullOrWhiteSpace(SearchText)
            ? _allCommands
            : _allCommands.Where(c => c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || 
                                     c.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var match in matches)
        {
            FilteredCommands.Add(match);
        }

        if (FilteredCommands.Count > 0)
        {
            SelectedCommand = FilteredCommands[0];
        }
    }

    private void ExecuteSelected()
    {
        if (SelectedCommand != null)
        {
            SelectedCommand.Action?.Invoke();
            IsVisible = false;
            SearchText = "";
        }
    }
}

public class PaletteCommand
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = ""; // Segoe Fluent Icon glyph
    public Action? Action { get; set; }
}
