using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Redball.UI.Converters;

/// <summary>
/// Converts boolean Active state to a brush color
/// </summary>
public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            return isActive
                ? new SolidColorBrush(Color.FromRgb(220, 53, 69))  // Red (Active)
                : new SolidColorBrush(Color.FromRgb(108, 117, 125)); // Gray (Paused)
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Cannot meaningfully convert Brush back to bool - one-way conversion only
        return System.Windows.Data.Binding.DoNothing;
    }
}

/// <summary>
/// Converts boolean to text based on parameter (format: "trueText|falseText")
/// </summary>
public class BoolToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string text)
        {
            var parts = text.Split('|');
            if (parts.Length == 2)
            {
                return boolValue ? parts[0] : parts[1];
            }
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Parse text back to bool by checking which part of parameter it matches
        if (value is string text && parameter is string paramText)
        {
            var parts = paramText.Split('|');
            if (parts.Length == 2)
            {
                if (text.Equals(parts[0], StringComparison.OrdinalIgnoreCase))
                    return true;
                if (text.Equals(parts[1], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        // Return DependencyProperty.UnsetValue to indicate conversion failure
        return System.Windows.DependencyProperty.UnsetValue;
    }
}

/// <summary>
/// Inverts a boolean value
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }
}

/// <summary>
/// Converts null/empty to Visibility
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = parameter?.ToString()?.ToLower() == "invert";
        var isNull = value == null || (value is string s && string.IsNullOrEmpty(s));
        var visible = invert ? !isNull : isNull;
        return visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Cannot meaningfully convert Visibility back to the original value - one-way conversion only
        return System.Windows.Data.Binding.DoNothing;
    }
}

/// <summary>
/// Inverts a boolean value and converts to Visibility (true -> Collapsed, false -> Visible)
/// </summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is System.Windows.Visibility v && v == System.Windows.Visibility.Collapsed;
    }
}
