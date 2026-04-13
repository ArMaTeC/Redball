using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Redball.UI.Services;
using System.Diagnostics;

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
        
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            foreach (var cmd in _allCommands) FilteredCommands.Add(cmd);
            return;
        }

        // 1. Check for Smart Intent
        var lowerSearch = SearchText.ToLower();
        var matches = new List<PaletteCommand>();
        
        // Timer Intent
        var timerMatch = System.Text.RegularExpressions.Regex.Match(SearchText, @"(?:awake|for|min|stay)\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (timerMatch.Success && int.TryParse(timerMatch.Groups[1].Value, out var mins))
        {
            if (mins > 0 && mins <= 720)
            {
                matches.Add(new PaletteCommand { Name = $"Start {mins}m Session", Description = $"Keep-Awake for {mins} minutes", Icon = "⏰", Action = () => KeepAwakeService.Instance.StartTimed(mins) });
            }
        }

        // Theme Intent
        if (lowerSearch.Contains("theme") || lowerSearch.Contains("mode") || lowerSearch.Contains("skin"))
        {
            var themes = new[] { "Dark", "Light", "Cyan", "Amber", "Red", "Green", "Slate" };
            foreach (var t in themes)
            {
                if (lowerSearch.Contains(t.ToLower()))
                {
                    matches.Add(new PaletteCommand { Name = $"Apply {t} Theme", Description = $"Switch UI color to {t}", Icon = "🎨", Action = () => ThemeManager.SetThemeFromConfig(t) });
                }
            }
        }

        // Documentation Intent
        if (lowerSearch.Contains("help") || lowerSearch.Contains("wiki") || lowerSearch.Contains("docs"))
        {
            matches.Add(new PaletteCommand { Name = "Open Documentation", Description = "View the Redball Wiki on GitHub", Icon = "📖", Action = () => Process.Start(new ProcessStartInfo("https://github.com/ArMaTeC/Redball/wiki") { UseShellExecute = true }) });
        }

        // 2. Direct name matches (High priority)
        matches.AddRange(_allCommands.Where(c => c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));

        // 3. Description/Keyword matches (Lower priority)
        var remaining = _allCommands.Except(matches)
                                   .Where(c => c.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        matches.AddRange(remaining);

        foreach (var match in matches.Distinct())
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
