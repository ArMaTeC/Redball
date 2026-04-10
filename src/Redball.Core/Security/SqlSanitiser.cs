namespace Redball.Core.Security;

using System;
using System.Text.RegularExpressions;

/// <summary>
/// Utility for sanitising SQL statements before logging to prevent information disclosure.
/// Removes or masks sensitive data from SQL statements.
/// </summary>
public static class SqlSanitiser
{
    /// <summary>
    /// Sanitises an SQL statement for safe logging by removing parameter values.
    /// </summary>
    /// <param name="sql">The SQL statement to sanitise.</param>
    /// <returns>A sanitised version safe for logging.</returns>
    public static string SanitiseForLogging(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        // Remove inline string literals (single quotes)
        sql = Regex.Replace(sql, @"'[^']*'", "'[REDACTED]'");

        // Remove inline numeric values after comparison operators
        sql = Regex.Replace(sql, @"(?<=[=<>])\s*\d+", " [REDACTED]");

        // Remove inline numeric values in IN clauses
        sql = Regex.Replace(sql, @"\bIN\s*\(\s*\d+[^)]*\)", "IN ([REDACTED])", RegexOptions.IgnoreCase);

        // Mask potential sensitive column names
        sql = MaskSensitiveColumns(sql);

        return sql.Trim();
    }

    /// <summary>
    /// Masks values in INSERT and UPDATE statements for safe logging.
    /// </summary>
    /// <param name="sql">The SQL statement to mask.</param>
    /// <returns>Masked SQL statement.</returns>
    public static string MaskInsertUpdateValues(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        var upper = sql.ToUpperInvariant();

        // Mask VALUES clause in INSERT statements
        if (upper.Contains("VALUES"))
        {
            sql = Regex.Replace(sql, @"VALUES\s*\([^)]+\)", "VALUES ([REDACTED])", RegexOptions.IgnoreCase);
        }

        // Mask SET clause values in UPDATE statements
        if (upper.Contains("SET"))
        {
            sql = Regex.Replace(sql, @"SET\s+[^=]+=\s*[^,]+", "[column]=[REDACTED]", RegexOptions.IgnoreCase);
        }

        return sql.Trim();
    }

    /// <summary>
    /// Masks sensitive column names in SQL statements.
    /// </summary>
    private static string MaskSensitiveColumns(string sql)
    {
        string[] sensitivePatterns = new[]
        {
            @"\bpassword\b",
            @"\bsecret\b",
            @"\btoken\b",
            @"\bapi[_-]?key\b",
            @"\bprivate[_-]?key\b",
            @"\bconnection[_-]?string\b",
            @"\bcredit[_-]?card\b",
            @"\bssn\b",
            @"\bsocial[_-]?security\b"
        };

        foreach (var pattern in sensitivePatterns)
        {
            sql = Regex.Replace(sql, pattern, "[SENSITIVE_COLUMN]", RegexOptions.IgnoreCase);
        }

        return sql;
    }

    /// <summary>
    /// Returns a generic description of the SQL operation type without the actual query.
    /// Use this when full sanitisation is not sufficient.
    /// </summary>
    /// <param name="sql">The SQL statement.</param>
    /// <returns>A generic operation description.</returns>
    public static string GetOperationDescription(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return "Empty SQL";

        var upper = sql.Trim().ToUpperInvariant();

        if (upper.StartsWith("SELECT"))
            return "SELECT operation";
        if (upper.StartsWith("INSERT"))
            return "INSERT operation";
        if (upper.StartsWith("UPDATE"))
            return "UPDATE operation";
        if (upper.StartsWith("DELETE"))
            return "DELETE operation";
        if (upper.StartsWith("CREATE"))
            return "CREATE operation";
        if (upper.StartsWith("DROP"))
            return "DROP operation";
        if (upper.StartsWith("ALTER"))
            return "ALTER operation";

        return "SQL operation";
    }
}
