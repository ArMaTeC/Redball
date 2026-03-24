using System;
using System.IO;
using System.Reflection;
using System.Windows.Data;

namespace Redball.UI.Converters;

/// <summary>
/// Converts relative asset paths to absolute paths based on the application's base directory.
/// This ensures Lottie animations and other assets can be found regardless of the current working directory.
/// </summary>
[ValueConversion(typeof(string), typeof(string))]
public class AssetPathConverter : IValueConverter
{
    private static readonly string _baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string relativePath)
            return value;

        // If already an absolute path, return as-is
        if (Path.IsPathRooted(relativePath))
            return relativePath;

        // Combine with base directory to create absolute path
        return Path.Combine(_baseDirectory, relativePath);
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException("AssetPathConverter does not support ConvertBack");
    }
}
