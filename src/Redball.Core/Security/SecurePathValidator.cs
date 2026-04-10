namespace Redball.Core.Security;

using System;
using System.IO;
using System.Security;

/// <summary>
/// Centralised path validation utility to prevent path traversal attacks.
/// All file path operations should use this class for validation.
/// </summary>
public static class SecurePathValidator
{
    /// <summary>
    /// Validates that a path is within a specified base directory.
    /// Protects against path traversal attacks using ".." sequences.
    /// </summary>
    /// <param name="path">The path to validate.</param>
    /// <param name="baseDir">The base directory that the path must be within.</param>
    /// <returns>True if the path is within the base directory.</returns>
    public static bool IsWithinDirectory(string path, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(baseDir))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullBase = Path.GetFullPath(baseDir);

            // Ensure base directory ends with separator for proper prefix matching
            if (!fullBase.EndsWith(Path.DirectorySeparatorChar.ToString()))
                fullBase += Path.DirectorySeparatorChar;

            return fullPath.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates a path and throws a SecurityException if it attempts path traversal.
    /// </summary>
    /// <param name="path">The path to validate.</param>
    /// <param name="baseDir">The base directory that the path must be within.</param>
    /// <param name="context">Context description for error messages.</param>
    /// <exception cref="SecurityException">Thrown when path traversal is detected.</exception>
    public static void ValidateNoTraversal(string path, string baseDir, string context = "Path")
    {
        if (!IsWithinDirectory(path, baseDir))
        {
            throw new SecurityException($"{context} escapes allowed directory: {path}");
        }
    }

    /// <summary>
    /// Validates that a path does not contain path traversal sequences.
    /// This is a simpler check that does not require a base directory.
    /// </summary>
    /// <param name="path">The path to validate.</param>
    /// <returns>True if no traversal sequences are found.</returns>
    public static bool ContainsNoTraversal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Check for common traversal patterns
        if (path.Contains("..") || path.Contains("~"))
            return false;

        // Check for rooted/absolute paths that might be unexpected
        if (Path.IsPathRooted(path) && !path.StartsWith("\\?\\"))
            return false;

        return true;
    }

    /// <summary>
    /// Sanitises a filename by removing or replacing dangerous characters.
    /// </summary>
    /// <param name="fileName">The filename to sanitise.</param>
    /// <returns>A sanitised filename safe for use.</returns>
    public static string SanitiseFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Filename cannot be empty", nameof(fileName));

        // Remove path separators and other dangerous characters
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            fileName = fileName.Replace(c, '_');
        }

        // Remove traversal sequences
        fileName = fileName.Replace("..", "_");

        return fileName.Trim();
    }

    /// <summary>
    /// Validates that a filename has an allowed extension.
    /// </summary>
    /// <param name="fileName">The filename to validate.</param>
    /// <param name="allowedExtensions">List of allowed extensions (e.g., ".txt", ".json").</param>
    /// <returns>True if the extension is in the allowed list.</returns>
    public static bool HasAllowedExtension(string fileName, string[] allowedExtensions)
    {
        if (string.IsNullOrWhiteSpace(fileName) || allowedExtensions == null || allowedExtensions.Length == 0)
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        foreach (var allowed in allowedExtensions)
        {
            if (extension == allowed.ToLowerInvariant())
                return true;
        }

        return false;
    }

    /// <summary>
    /// Creates a validated combined path that is guaranteed to be within the base directory.
    /// </summary>
    /// <param name="baseDir">The base directory.</param>
    /// <param name="relativePath">The relative path to combine.</param>
    /// <returns>The combined and validated full path.</returns>
    /// <exception cref="SecurityException">Thrown when the combined path would escape the base directory.</exception>
    public static string CreateValidatedPath(string baseDir, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            throw new ArgumentException("Base directory cannot be empty", nameof(baseDir));

        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Relative path cannot be empty", nameof(relativePath));

        // Prevent absolute paths in the relative component
        if (Path.IsPathRooted(relativePath))
            throw new SecurityException("Relative path cannot be absolute");

        var combined = Path.Combine(baseDir, relativePath);
        ValidateNoTraversal(combined, baseDir, "Combined path");

        return combined;
    }
}
