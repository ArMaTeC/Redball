namespace Redball.Core.Security;

using System;

/// <summary>
/// Utility for handling exceptions securely to prevent information disclosure.
/// Ensures detailed exception details are logged internally but generic messages are shown to users.
/// </summary>
public static class SafeExceptionHandler
{
    /// <summary>
    /// Handles an exception securely, logging full details internally while returning a safe user-facing message.
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="context">Context description for logging.</param>
    /// <param name="logger">Optional logger action for internal logging.</param>
    /// <param name="userMessage">Optional custom user-facing message. If null, a generic message is used.</param>
    /// <returns>A safe, non-revealing error message suitable for user display.</returns>
    public static string Handle(Exception ex, string context, Action<string, string, Exception>? logger = null, string? userMessage = null)
    {
        // Log full exception details internally
        logger?.Invoke(context, $"Error in {context}: {ex.Message}", ex);

        // Return safe, generic message to user
        return userMessage ?? "An error occurred. Please try again or contact support if the problem persists.";
    }

    /// <summary>
    /// Returns a generic error message, hiding internal exception details.
    /// </summary>
    public static string GetSafeErrorMessage(string? customMessage = null)
    {
        return customMessage ?? "Operation failed";
    }

    /// <summary>
    /// Returns a generic error message for a specific operation type.
    /// </summary>
    public static string GetSafeErrorMessageForOperation(string operation)
    {
        return $"{operation} failed. Please try again later.";
    }

    /// <summary>
    /// Sanitises an error message to remove potentially sensitive information.
    /// Removes paths, connection strings, and other internal details.
    /// </summary>
    public static string SanitiseErrorMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        // Remove file paths
        message = System.Text.RegularExpressions.Regex.Replace(message, @"[a-zA-Z]:\\[^\s]*|/[^\s]*", "[PATH]");

        // Remove connection strings
        message = System.Text.RegularExpressions.Regex.Replace(message, @"(Server|Data Source|Host)=[^;]*", "[SERVER]");
        message = System.Text.RegularExpressions.Regex.Replace(message, @"(Database|Initial Catalog)=[^;]*", "[DATABASE]");
        message = System.Text.RegularExpressions.Regex.Replace(message, @"(User Id|Password)=[^;]*", "[CREDENTIALS]");

        // Remove stack trace indicators
        if (message.Contains(" at ") && message.Contains(" in "))
        {
            var index = message.IndexOf(" at ");
            message = message.Substring(0, index).Trim();
        }

        return message;
    }
}
