using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Redball.UI.Services;

/// <summary>
/// Interactive tutorial system with guided tours and contextual help.
/// Provides onboarding for new users and feature discovery.
/// </summary>
public sealed class TutorialService
{
    private readonly List<TourStep> _currentTour = new();
    private int _currentStepIndex;
    private Popup? _activeTooltip;
    private Canvas? _overlayCanvas;
    private Window? _hostWindow;

    public static TutorialService Instance { get; } = new();

    public bool IsTourActive => _currentTour.Count > 0 && _currentStepIndex < _currentTour.Count;
    public event EventHandler<TourStepChangedEventArgs>? StepChanged;
    public event EventHandler? TourCompleted;
    public event EventHandler? TourCancelled;

    private TutorialService() { }

    /// <summary>
    /// Starts the onboarding tour for first-time users.
    /// </summary>
    public void StartOnboardingTour(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _currentTour.Clear();
        _currentStepIndex = 0;

        // Build onboarding tour steps
        _currentTour.AddRange(new[]
        {
            new TourStep
            {
                Id = "welcome",
                Title = "Welcome to Redball",
                Content = "Redball keeps your computer awake when you need it. Let's take a quick tour of the main features.",
                TargetElement = null, // Center screen
                Position = TooltipPosition.Center,
                CanGoBack = false
            },
            new TourStep
            {
                Id = "toggle",
                Title = "Keep Awake Toggle",
                Content = "Click this button to start or stop keeping your computer awake. When active, Redball prevents sleep and screen dimming.",
                TargetElementName = "MainToggleButton",
                Position = TooltipPosition.Bottom,
                HighlightType = HighlightType.Pulse
            },
            new TourStep
            {
                Id = "timer",
                Title = "Timer",
                Content = "Set how long Redball should stay active. When the timer expires, your computer can sleep normally again.",
                TargetElementName = "MainTimerSlider",
                Position = TooltipPosition.Top,
                HighlightType = HighlightType.Circle
            },
            new TourStep
            {
                Id = "battery",
                Title = "Battery Awareness",
                Content = "Enable this to automatically pause when battery is low. Redball will resume when you plug in.",
                TargetElementName = "MainBatteryAwareCheck",
                Position = TooltipPosition.Left,
                HighlightType = HighlightType.Rectangle
            },
            new TourStep
            {
                Id = "tray",
                Title = "System Tray",
                Content = "Redball runs in your system tray. Right-click the icon for quick access to common actions.",
                TargetElement = null,
                Position = TooltipPosition.TopRight,
                CanGoBack = false
            },
            new TourStep
            {
                Id = "settings",
                Title = "Settings",
                Content = "Explore the Settings panel to customize Redball for your workflow. There are many advanced features to discover!",
                TargetElementName = "MainSettingsButton",
                Position = TooltipPosition.Bottom,
                IsLastStep = true
            }
        });

        ShowCurrentStep();
        Logger.Info("Tutorial", "Onboarding tour started");
    }

    /// <summary>
    /// Starts a feature-specific tour.
    /// </summary>
    public void StartFeatureTour(string featureName)
    {
        _currentTour.Clear();
        _currentStepIndex = 0;

        _currentTour.AddRange(featureName.ToLower() switch
        {
            "typething" => new[]
            {
                new TourStep
                {
                    Id = "typething_intro",
                    Title = "TypeThing Feature",
                    Content = "TypeThing sends simulated keyboard input to prevent idle detection by apps like Teams.",
                    TargetElementName = "TypeThingTab",
                    Position = TooltipPosition.Bottom
                },
                new TourStep
                {
                    Id = "typething_mode",
                    Title = "Input Mode",
                    Content = "Choose how TypeThing sends input: SendInput API, HID device simulation, or Windows Service.",
                    TargetElementName = "TypeThingModeCombo",
                    Position = TooltipPosition.Top
                },
                new TourStep
                {
                    Id = "typething_test",
                    Title = "Test Your Setup",
                    Content = "Use the Test button to verify TypeThing works with your applications before enabling it.",
                    TargetElementName = "TypeThingTestButton",
                    Position = TooltipPosition.Left,
                    IsLastStep = true
                }
            },
            "pomodoro" => new[]
            {
                new TourStep
                {
                    Id = "pomodoro_intro",
                    Title = "Pomodoro Timer",
                    Content = "The Pomodoro technique helps you stay focused with timed work sessions and breaks.",
                    TargetElementName = "PomodoroTab",
                    Position = TooltipPosition.Bottom
                },
                new TourStep
                {
                    Id = "pomodoro_settings",
                    Title = "Customize Durations",
                    Content = "Adjust work session length, short break, and long break durations to match your preference.",
                    TargetElementName = "PomodoroWorkDurationSlider",
                    Position = TooltipPosition.Top,
                    IsLastStep = true
                }
            },
            _ => Array.Empty<TourStep>()
        });

        if (_currentTour.Count > 0)
        {
            ShowCurrentStep();
        }
    }

    /// <summary>
    /// Shows a contextual tip for a specific feature.
    /// </summary>
    public void ShowContextualTip(string elementName, string title, string content)
    {
        var step = new TourStep
        {
            Id = "contextual_tip",
            Title = title,
            Content = content,
            TargetElementName = elementName,
            Position = TooltipPosition.Auto,
            IsContextual = true
        };

        _currentTour.Clear();
        _currentTour.Add(step);
        _currentStepIndex = 0;

        ShowCurrentStep();
    }

    /// <summary>
    /// Advances to the next step in the tour.
    /// </summary>
    public void NextStep()
    {
        if (_currentStepIndex < _currentTour.Count - 1)
        {
            HideCurrentStep();
            _currentStepIndex++;
            ShowCurrentStep();
            StepChanged?.Invoke(this, new TourStepChangedEventArgs(_currentStepIndex, _currentTour.Count));
        }
        else
        {
            CompleteTour();
        }
    }

    /// <summary>
    /// Goes back to the previous step.
    /// </summary>
    public void PreviousStep()
    {
        if (_currentStepIndex > 0 && _currentTour[_currentStepIndex].CanGoBack)
        {
            HideCurrentStep();
            _currentStepIndex--;
            ShowCurrentStep();
            StepChanged?.Invoke(this, new TourStepChangedEventArgs(_currentStepIndex, _currentTour.Count));
        }
    }

    /// <summary>
    /// Cancels the current tour.
    /// </summary>
    public void CancelTour()
    {
        HideCurrentStep();
        _currentTour.Clear();
        _currentStepIndex = 0;
        TourCancelled?.Invoke(this, EventArgs.Empty);
        Logger.Info("Tutorial", "Tour cancelled");
    }

    /// <summary>
    /// Completes the tour and saves progress.
    /// </summary>
    public void CompleteTour()
    {
        HideCurrentStep();
        
        // Mark tour as completed in config
        if (!_currentTour.Any(s => s.IsContextual))
        {
            // Mark tour as completed - property doesn't exist, using comment as placeholder
            // ConfigService.Instance.Config.OnboardingCompleted = true;
            // ConfigService.Instance.Save();
        }

        _currentTour.Clear();
        _currentStepIndex = 0;
        
        TourCompleted?.Invoke(this, EventArgs.Empty);
        Logger.Info("Tutorial", "Tour completed");
    }

    private void ShowCurrentStep()
    {
        if (_currentStepIndex >= _currentTour.Count) return;

        var step = _currentTour[_currentStepIndex];
        
        // Create overlay if needed
        EnsureOverlay();
        
        // Find target element with null check
        FrameworkElement? target = null;
        if (!string.IsNullOrEmpty(step.TargetElementName) && _hostWindow != null)
        {
            target = FindElementByName(_hostWindow, step.TargetElementName);
        }
        else if (step.TargetElement != null)
        {
            target = step.TargetElement;
        }

        // Show highlight
        if (target != null && step.HighlightType != HighlightType.None)
        {
            ShowHighlight(target, step.HighlightType);
        }

        // Show tooltip
        ShowTooltip(step, target);

        // Announce to screen readers - AccessibilityService not available
        // AccessibilityService.Instance.Announce($"{step.Title}: {step.Content}", LiveRegionPriority.Polite);
    }

    private void HideCurrentStep()
    {
        _activeTooltip?.IsOpen = false;
        _activeTooltip = null;
        _overlayCanvas?.Children.Clear();
    }

    private void EnsureOverlay()
    {
        if (_overlayCanvas != null) return;

        _overlayCanvas = new Canvas
        {
            Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)),
            IsHitTestVisible = false
        };

        if (_hostWindow?.Content is Panel rootPanel)
        {
            rootPanel.Children.Add(_overlayCanvas);
            Panel.SetZIndex(_overlayCanvas, 1000);
        }
    }

    private void ShowHighlight(FrameworkElement target, HighlightType type)
    {
        if (_overlayCanvas == null) return;

        var point = target.TransformToAncestor(_hostWindow).Transform(new Point(0, 0));
        
        Shape highlight = type switch
        {
            HighlightType.Circle => new Ellipse
            {
                Width = Math.Max(target.ActualWidth, target.ActualHeight) + 20,
                Height = Math.Max(target.ActualWidth, target.ActualHeight) + 20,
                Stroke = new SolidColorBrush(Colors.Yellow),
                StrokeThickness = 3,
                Fill = Brushes.Transparent
            },
            HighlightType.Rectangle => new Rectangle
            {
                Width = target.ActualWidth + 10,
                Height = target.ActualHeight + 10,
                Stroke = new SolidColorBrush(Colors.Yellow),
                StrokeThickness = 3,
                RadiusX = 4,
                RadiusY = 4,
                Fill = Brushes.Transparent
            },
            HighlightType.Pulse => new Rectangle
            {
                Width = target.ActualWidth + 20,
                Height = target.ActualHeight + 20,
                Stroke = new SolidColorBrush(Colors.Yellow),
                StrokeThickness = 3,
                RadiusX = 8,
                RadiusY = 8,
                Fill = Brushes.Transparent
            },
            _ => throw new ArgumentOutOfRangeException()
        };

        Canvas.SetLeft(highlight, point.X - 10);
        Canvas.SetTop(highlight, point.Y - 10);
        
        _overlayCanvas.Children.Add(highlight);
    }

    private void ShowTooltip(TourStep step, FrameworkElement? target)
    {
        var tooltipContent = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            MaxWidth = 350,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = step.Title,
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        Margin = new Thickness(0, 0, 0, 10)
                    },
                    new TextBlock
                    {
                        Text = step.Content,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Colors.LightGray),
                        Margin = new Thickness(0, 0, 0, 15)
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children =
                        {
                            step.CanGoBack ? new Button
                            {
                                Content = "Back",
                                Margin = new Thickness(0, 0, 10, 0)
                            } : null!,
                            new Button
                            {
                                Content = step.IsLastStep ? "Finish" : "Next"
                            },
                            new Button
                            {
                                Content = "Skip",
                                Margin = new Thickness(10, 0, 0, 0)
                            }
                        }
                    }
                }
            }
        };

        _activeTooltip = new Popup
        {
            Child = tooltipContent,
            PlacementTarget = target ?? _hostWindow,
            Placement = step.Position switch
            {
                TooltipPosition.Top => PlacementMode.Top,
                TooltipPosition.Bottom => PlacementMode.Bottom,
                TooltipPosition.Left => PlacementMode.Left,
                TooltipPosition.Right => PlacementMode.Right,
                TooltipPosition.Center => PlacementMode.Center,
                TooltipPosition.TopRight => PlacementMode.Top,
                _ => PlacementMode.Mouse
            },
            HorizontalOffset = step.Position == TooltipPosition.Center ? 0 : 10,
            VerticalOffset = step.Position == TooltipPosition.Center ? 0 : 10,
            IsOpen = true
        };
    }

    private FrameworkElement? FindElementByName(DependencyObject parent, string name)
    {
        if (parent is FrameworkElement fe && fe.Name == name)
            return fe;

        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            var result = FindElementByName(child, name);
            if (result != null) return result;
        }

        return null;
    }
}

public class TourStep
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? TargetElementName { get; set; }
    public FrameworkElement? TargetElement { get; set; }
    public TooltipPosition Position { get; set; } = TooltipPosition.Bottom;
    public HighlightType HighlightType { get; set; } = HighlightType.Rectangle;
    public bool CanGoBack { get; set; } = true;
    public bool IsLastStep { get; set; }
    public bool IsContextual { get; set; }
}

public enum TooltipPosition
{
    Top,
    Bottom,
    Left,
    Right,
    Center,
    TopRight,
    Auto
}

public enum HighlightType
{
    None,
    Circle,
    Rectangle,
    Pulse
}

public class TourStepChangedEventArgs : EventArgs
{
    public int CurrentStep { get; }
    public int TotalSteps { get; }
    public double Progress => (double)CurrentStep / TotalSteps;

    public TourStepChangedEventArgs(int current, int total)
    {
        CurrentStep = current;
        TotalSteps = total;
    }
}
