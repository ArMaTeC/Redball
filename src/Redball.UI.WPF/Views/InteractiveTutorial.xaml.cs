using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Interactive tutorial for onboarding users to Redball features.
/// </summary>
public partial class InteractiveTutorial : Window
{
    private int _currentStep = 0;
    private readonly string[] _stepTitles = new[]
    {
        "Welcome to Redball!",
        "System Tray Control",
        "Keyboard Shortcuts",
        "Smart Features",
        "You're Ready!"
    };
    
    private readonly string[] _stepDescriptions = new[]
    {
        "Redball keeps your Windows PC awake when you need it. Let's learn the basics in just 5 quick steps.",
        "Redball lives in your system tray. Right-click the red ball icon to access all features and settings.",
        "Press SPACE to toggle keep-awake, D for display settings, H for heartbeat, and I for indefinite mode.",
        "Enable Battery-Aware, Network-Aware, or Idle Detection modes to automatically manage your PC's state.",
        "You're all set! Redball will keep your PC awake so you can focus on what matters. Happy working!"
    };

    public InteractiveTutorial()
    {
        InitializeComponent();
        UpdateStep();
    }

    private void UpdateStep()
    {
        StepNumber.Text = $"Step {_currentStep + 1} of 5";
        StepTitle.Text = _stepTitles[_currentStep];
        StepDescription.Text = _stepDescriptions[_currentStep];
        TutorialProgress.Value = _currentStep + 1;
        
        // Update dots
        UpdateDots();
        
        // Update buttons
        PreviousButton.IsEnabled = _currentStep > 0;
        NextButton.Content = _currentStep == 4 ? "Finish 🎉" : "Next →";
        
        // Update demo content based on step
        UpdateDemoContent();
        
        // Animate transition
        AnimateContentTransition();
    }

    private void UpdateDots()
    {
        var dots = new[] { Dot1, Dot2, Dot3, Dot4, Dot5 };
        var accentBrush = (Brush)FindResource("AccentColor");
        var borderBrush = (Brush)FindResource("BorderBrush");
        
        for (int i = 0; i < dots.Length; i++)
        {
            dots[i].Fill = i <= _currentStep ? accentBrush : borderBrush;
        }
    }

    private void UpdateDemoContent()
    {
        DemoContent.Children.Clear();
        
        switch (_currentStep)
        {
            case 0:
                // Welcome - show logo animation
                DemoText.Text = "Redball prevents sleep, screen lock, and idle timeouts";
                DemoText.HorizontalAlignment = HorizontalAlignment.Center;
                DemoText.VerticalAlignment = VerticalAlignment.Center;
                DemoContent.Children.Add(DemoText);
                TryItButton.Visibility = Visibility.Collapsed;
                break;
                
            case 1:
                // Tray icon demo
                DemoText.Text = "🔴 ← Look for this in your system tray (near the clock)";
                DemoContent.Children.Add(DemoText);
                TryItButton.Content = "Show Me";
                TryItButton.Visibility = Visibility.Visible;
                TryItButton.Click -= TryItButton_Click;
                TryItButton.Click += (s, e) => 
                {
                    // Would show tray notification in real implementation
                    NotificationWindow.Show("Try It", "Right-click the red ball in your system tray!", "\uE73E");
                };
                break;
                
            case 2:
                // Keyboard shortcuts demo
                var shortcutsPanel = new StackPanel 
                { 
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center 
                };
                
                shortcutsPanel.Children.Add(new TextBlock 
                { 
                    Text = "SPACE = Toggle", FontSize = 16, FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 5, 0, 5) 
                });
                shortcutsPanel.Children.Add(new TextBlock 
                { 
                    Text = "D = Display Sleep", FontSize = 14, Opacity = 0.8,
                    Margin = new Thickness(0, 3, 0, 3) 
                });
                shortcutsPanel.Children.Add(new TextBlock 
                { 
                    Text = "H = F15 Heartbeat", FontSize = 14, Opacity = 0.8,
                    Margin = new Thickness(0, 3, 0, 3) 
                });
                shortcutsPanel.Children.Add(new TextBlock 
                { 
                    Text = "I = Indefinite Mode", FontSize = 14, Opacity = 0.8,
                    Margin = new Thickness(0, 3, 0, 3) 
                });
                
                DemoContent.Children.Add(shortcutsPanel);
                TryItButton.Visibility = Visibility.Collapsed;
                break;
                
            case 3:
                // Smart features demo
                var featuresPanel = new StackPanel 
                { 
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center 
                };
                
                featuresPanel.Children.Add(new TextBlock 
                { 
                    Text = "🔋 Battery-Aware", FontSize = 14, Margin = new Thickness(0, 3, 0, 3) 
                });
                featuresPanel.Children.Add(new TextBlock 
                { 
                    Text = "🌐 Network-Aware", FontSize = 14, Margin = new Thickness(0, 3, 0, 3) 
                });
                featuresPanel.Children.Add(new TextBlock 
                { 
                    Text = "💤 Idle Detection", FontSize = 14, Margin = new Thickness(0, 3, 0, 3) 
                });
                featuresPanel.Children.Add(new TextBlock 
                { 
                    Text = "📅 Scheduled Operation", FontSize = 14, Margin = new Thickness(0, 3, 0, 3) 
                });
                
                DemoContent.Children.Add(featuresPanel);
                TryItButton.Content = "Open Settings";
                TryItButton.Visibility = Visibility.Visible;
                break;
                
            case 4:
                // Completion
                DemoText.Text = "🎉 You're ready to use Redball!";
                DemoText.FontSize = 20;
                DemoText.FontWeight = FontWeights.Bold;
                DemoContent.Children.Add(DemoText);
                TryItButton.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void AnimateContentTransition()
    {
        // Fade in animation
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        
        var slideUp = new ThicknessAnimation
        {
            From = new Thickness(0, 20, 0, 0),
            To = new Thickness(0),
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        
        StepTitle.BeginAnimation(OpacityProperty, fadeIn);
        StepDescription.BeginAnimation(OpacityProperty, fadeIn);
        DemoArea.BeginAnimation(OpacityProperty, fadeIn);
        DemoArea.BeginAnimation(MarginProperty, slideUp);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep < 4)
        {
            _currentStep++;
            UpdateStep();
        }
        else
        {
            // Mark tutorial as completed
            ConfigService.Instance.Config.FirstRun = false;
            ConfigService.Instance.Save();
            
            DialogResult = true;
            Close();
        }
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 0)
        {
            _currentStep--;
            UpdateStep();
        }
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        var result = NotificationWindow.Show(
            "Skip Tutorial",
            "Skip the tutorial? You can always access it from Settings → Help.",
            "\uE897", // Help/Info icon
            true);
        
        if (result)
        {
            ConfigService.Instance.Config.FirstRun = false;
            ConfigService.Instance.Save();
            
            DialogResult = false;
            Close();
        }
    }

    private void TryItButton_Click(object sender, RoutedEventArgs e)
    {
        // Placeholder for interactive actions
    }
}
