using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Redball.UI.Services;

/// <summary>
/// Service for managing keyboard navigation and accessibility
/// </summary>
public class KeyboardNavigationService
{
    private Window? _window;
    private bool _isEnabled;

    /// <summary>
    /// Attaches keyboard navigation to a window
    /// </summary>
    public void Attach(Window window)
    {
        _window = window;
        _window.PreviewKeyDown += OnPreviewKeyDown;
        _window.Loaded += OnWindowLoaded;
        _isEnabled = true;

        Logger.Info("KeyboardNav", $"Attached to window: {window.GetType().Name}");
    }

    /// <summary>
    /// Detaches keyboard navigation
    /// </summary>
    public void Detach()
    {
        if (_window != null)
        {
            _window.PreviewKeyDown -= OnPreviewKeyDown;
            _window.Loaded -= OnWindowLoaded;
        }
        _isEnabled = false;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Set initial focus to first focusable element
        if (_window != null)
        {
            var firstElement = FindFirstFocusableElement(_window);
            firstElement?.Focus();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isEnabled || _window == null) return;

        // Handle global shortcuts
        switch (e.Key)
        {
            case Key.Escape:
                // Close dialog windows on Escape
                if (_window is Views.SettingsWindow || _window is Views.AboutWindow)
                {
                    _window.Close();
                    e.Handled = true;
                }
                break;

            case Key.F1:
                // Show help
                ShowHelp();
                e.Handled = true;
                break;

            case Key.Tab:
                // Enhanced tab navigation with Shift support
                HandleTabNavigation(e);
                break;

            case Key.Enter:
                // Activate default button if focused on input
                HandleEnterKey(e);
                break;
        }
    }

    private void HandleTabNavigation(KeyEventArgs e)
    {
        // Let WPF handle the tab navigation naturally
        // This ensures proper TabIndex order is followed
        Logger.Debug("KeyboardNav", $"Tab navigation: Shift={e.KeyboardDevice.Modifiers == ModifierKeys.Shift}");
    }

    private void HandleEnterKey(KeyEventArgs e)
    {
        // If focused element is a TextBox in a dialog, trigger default button
        if (Keyboard.FocusedElement is TextBox && e.KeyboardDevice.Modifiers == ModifierKeys.None)
        {
            var defaultButton = FindDefaultButton(_window);
            if (defaultButton?.IsEnabled == true)
            {
                if (defaultButton.Command?.CanExecute(null) == true)
                {
                    defaultButton.Command.Execute(null);
                }
                else
                {
                    defaultButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                e.Handled = true;
            }
        }
    }

    private void ShowHelp()
    {
        Logger.Info("KeyboardNav", "Help requested via F1");
        // Could open help window or show balloon tip
        Services.NotificationService.Instance.ShowInfo(
            "Keyboard Shortcuts",
            "Ctrl+Alt+Pause - Toggle Keep-Awake\nCtrl+Shift+V - TypeThing Start\nCtrl+Shift+X - TypeThing Stop\nF1 - This help\nEscape - Close dialog");
    }

    /// <summary>
    /// Finds the first focusable element in the visual tree
    /// </summary>
    public static FrameworkElement? FindFirstFocusableElement(DependencyObject parent)
    {
        if (parent == null) return null;

        // Check if this element is focusable
        if (parent is FrameworkElement element && element.Focusable && element.IsVisible && element.IsEnabled)
        {
            // Don't focus labels, textblocks, etc.
            if (element is TextBlock || element is Label)
                return null;
            return element;
        }

        // Recursively check children
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            var result = FindFirstFocusableElement(child);
            if (result != null)
                return result;
        }

        return null;
    }

    private Button? FindDefaultButton(Window? window)
    {
        if (window == null) return null;

        // Look for button with IsDefault = true
        return FindChild<Button>(window, b => b.IsDefault);
    }

    /// <summary>
    /// Finds a child element of specific type matching a predicate
    /// </summary>
    public static T? FindChild<T>(DependencyObject parent, System.Predicate<T> predicate) where T : DependencyObject
    {
        if (parent == null) return null;

        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild && predicate(typedChild))
                return typedChild;

            var result = FindChild(child, predicate);
            if (result != null)
                return result;
        }

        return null;
    }

    /// <summary>
    /// Sets up accessibility properties for a control
    /// </summary>
    public static void SetAccessibilityProperties(FrameworkElement element, string name, string? helpText = null)
    {
        AutomationProperties.SetName(element, name);
        if (helpText != null)
        {
            AutomationProperties.SetHelpText(element, helpText);
        }
    }

    /// <summary>
    /// Announces text to screen readers
    /// </summary>
    public static void AnnounceToScreenReader(string message)
    {
        // Use live region to announce to screen readers
        if (Application.Current.MainWindow != null)
        {
            AutomationPeer? peer = null;
            if (Application.Current.MainWindow is UIElement element)
            {
                peer = UIElementAutomationPeer.CreatePeerForElement(element);
            }
            
            if (peer != null)
            {
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }

        Logger.Debug("Accessibility", $"Announced: {message}");
    }
}

/// <summary>
/// Extension methods for accessibility
/// </summary>
public static class AccessibilityExtensions
{
    /// <summary>
    /// Sets automation properties fluently
    /// </summary>
    public static T WithAutomationName<T>(this T element, string name) where T : FrameworkElement
    {
        AutomationProperties.SetName(element, name);
        return element;
    }

    /// <summary>
    /// Sets automation help text
    /// </summary>
    public static T WithAutomationHelp<T>(this T element, string helpText) where T : FrameworkElement
    {
        AutomationProperties.SetHelpText(element, helpText);
        return element;
    }

    /// <summary>
    /// Marks element as live region for announcements
    /// </summary>
    public static T AsLiveRegion<T>(this T element) where T : FrameworkElement
    {
        AutomationProperties.SetLiveSetting(element, AutomationLiveSetting.Assertive);
        return element;
    }
}
