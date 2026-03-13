using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Redball.UI.Services;
using Redball.UI.Views;

namespace Redball.UI.ViewModels;

/// <summary>
/// Main ViewModel for Redball v3.0 WPF UI
/// Handles state management and command binding
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private bool _isActive = true;
    private string _statusText = "Active | Display On | F15 On";
    private bool _isDarkMode = true;
    private WeakReference<MainWindow>? _mainWindowRef;

    public MainViewModel()
    {
        Logger.Info("MainViewModel", "Constructor called");
        
        ToggleActiveCommand = new RelayCommand(ToggleActive);
        PauseKeepAwakeCommand = new RelayCommand(ToggleActive);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ExitCommand = new RelayCommand(ExitApplication);
        ShowAboutCommand = new RelayCommand(ShowAbout);
        TypeThingCommand = new RelayCommand(StartTypeThing);
        
        Logger.Info("MainViewModel", "Commands initialized");
    }

    /// <summary>
    /// Sets the reference to the MainWindow for delegating window operations
    /// </summary>
    public void SetMainWindow(MainWindow window)
    {
        Logger.Debug("MainViewModel", "Setting MainWindow reference");
        _mainWindowRef = new WeakReference<MainWindow>(window);
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                Logger.Info("MainViewModel", $"IsActive changed: {_isActive} -> {value}");
                _isActive = value;
                OnPropertyChanged();
                UpdateStatusText();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText != value)
            {
                Logger.Verbose("MainViewModel", $"StatusText changed: '{_statusText}' -> '{value}'");
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode != value)
            {
                Logger.Info("MainViewModel", $"IsDarkMode changed: {_isDarkMode} -> {value}");
                _isDarkMode = value;
                OnPropertyChanged();
                ThemeManager.SetTheme(value ? Theme.Dark : Theme.Light);
            }
        }
    }

    public ICommand ToggleActiveCommand { get; }
    public ICommand PauseKeepAwakeCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand ShowAboutCommand { get; }
    public ICommand TypeThingCommand { get; }

    private void ToggleActive()
    {
        IsActive = !IsActive;
        Logger.Info("MainViewModel", $"ToggleActive called, new state: {IsActive}");
        
        // Send IPC message to PowerShell core
        Task.Run(async () =>
        {
            Logger.Debug("MainViewModel", "Sending ToggleActive IPC message...");
            var success = await IpcClientService.SendToPowerShellAsync("ToggleActive", new { Active = IsActive });
            if (!success)
            {
                Logger.Warning("MainViewModel", "Failed to notify PowerShell core of state change");
            }
            else
            {
                Logger.Debug("MainViewModel", "PowerShell core notified successfully");
            }
        });
    }

    private void OpenSettings()
    {
        Logger.Info("MainViewModel", "OpenSettings called");
        
        // Delegate to MainWindow to show settings properly
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            Logger.Debug("MainViewModel", "Delegating to MainWindow");
            mainWindow.ShowSettings();
        }
        else
        {
            Logger.Warning("MainViewModel", "MainWindow reference not available, creating directly");
            var settingsWindow = new Views.SettingsWindow(null);
            settingsWindow.Show();
        }
    }

    private void StartTypeThing()
    {
        Logger.Info("MainViewModel", "StartTypeThing called");
        
        // Delegate to MainWindow for TypeThing paste-as-typing
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            Logger.Debug("MainViewModel", "Delegating TypeThing to MainWindow");
            mainWindow.StartTypeThing();
        }
        else
        {
            Logger.Warning("MainViewModel", "MainWindow reference not available, cannot start TypeThing");
        }
    }

    private void ShowAbout()
    {
        Logger.Info("MainViewModel", "ShowAbout called");
        
        // Delegate to MainWindow to show about properly
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            Logger.Debug("MainViewModel", "Delegating to MainWindow");
            mainWindow.ShowAbout();
        }
        else
        {
            Logger.Warning("MainViewModel", "MainWindow reference not available, creating directly");
            var aboutWindow = new Views.AboutWindow();
            aboutWindow.Show();
        }
    }

    private void ExitApplication()
    {
        Logger.Info("MainViewModel", "ExitApplication called");
        
        // Confirm exit when keep-awake is active
        if (_isActive)
        {
            Logger.Debug("MainViewModel", "Showing exit confirmation dialog");
            
            // Get the currently active window to use as owner (handles About/Settings windows being open)
            Window? ownerWindow = null;
            if (Application.Current.Windows.Count > 0)
            {
                // Find the active window or fall back to main window
                foreach (Window window in Application.Current.Windows)
                {
                    if (window.IsActive && window.IsVisible)
                    {
                        ownerWindow = window;
                        break;
                    }
                }
            }
            // Fall back to main window if no active window found
            if (ownerWindow == null)
            {
                ownerWindow = _mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mw) 
                    ? mw 
                    : Application.Current.MainWindow;
            }

            var result = System.Windows.MessageBox.Show(
                ownerWindow,
                "Redball is currently keeping your system awake. Are you sure you want to exit?",
                "Confirm Exit",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
                
            if (result != MessageBoxResult.OK)
            {
                Logger.Info("MainViewModel", "User cancelled exit");
                return;
            }
            
            Logger.Info("MainViewModel", "User confirmed exit");
        }

        // Dispose tray icon before shutdown if we have a reference
        if (_mainWindowRef != null && _mainWindowRef.TryGetTarget(out var mainWindow))
        {
            Logger.Debug("MainViewModel", "Delegating exit to MainWindow");
            mainWindow.ExitApplication();
        }
        else
        {
            Logger.Info("MainViewModel", "Shutting down application directly");
            System.Windows.Application.Current.Shutdown();
        }
    }

    private void UpdateStatusText()
    {
        StatusText = IsActive
            ? "Active | Display On | F15 On"
            : "Paused | Display Normal | F15 Off";
        Logger.Debug("MainViewModel", $"StatusText updated to: {StatusText}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        Logger.Verbose("MainViewModel", $"Property changed: {propertyName}");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Simple relay command implementation
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }
}
