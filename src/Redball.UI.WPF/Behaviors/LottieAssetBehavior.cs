using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Animation;
using LottieSharp.WPF;

namespace Redball.UI.Behaviors;

/// <summary>
/// Attached behavior for LottieAnimationView that converts relative asset paths to absolute paths.
/// This ensures animations can be found regardless of the current working directory.
/// </summary>
public static class LottieAssetBehavior
{
    private static readonly string _baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    public static readonly DependencyProperty RelativeFileNameProperty = DependencyProperty.RegisterAttached(
        "RelativeFileName",
        typeof(string),
        typeof(LottieAssetBehavior),
        new PropertyMetadata(null, OnRelativeFileNameChanged));

    public static string? GetRelativeFileName(DependencyObject obj)
        => (string?)obj.GetValue(RelativeFileNameProperty);

    public static void SetRelativeFileName(DependencyObject obj, string? value)
        => obj.SetValue(RelativeFileNameProperty, value);

    private static void OnRelativeFileNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not LottieAnimationView lottieView)
            return;

        var relativePath = (string?)e.NewValue;
        if (string.IsNullOrEmpty(relativePath))
            return;

        // Convert to absolute path
        var absolutePath = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(_baseDirectory, relativePath);

        // Set the FileName property after the control is loaded
        if (lottieView.IsLoaded)
        {
            lottieView.FileName = absolutePath;
        }
        else
        {
            // Wait for loaded event
            RoutedEventHandler? handler = null;
            handler = (s, _) =>
            {
                lottieView.Loaded -= handler;
                lottieView.FileName = absolutePath;
            };
            lottieView.Loaded += handler;
        }
    }
}
