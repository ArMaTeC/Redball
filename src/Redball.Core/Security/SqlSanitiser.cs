namespace Redball.Core.Security;

using System;
using System.Text.RegularExpressions;

/// <summary>
/// Utility for sanitising SQL statements before logging to prevent information disclosure.
/// Removes or masks sensitive data from SQL statements.
/// </summary>
public static partial class SqlSanitiser
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
        sql = StringLiteralRegex().Replace(sql, "'[REDACTED]'");

        // Remove inline numeric values after comparison operators
        sql = ComparisonNumericRegex().Replace(sql, " [REDACTED]");

        // Remove inline numeric values in IN clauses
        sql = InClauseNumericRegex().Replace(sql, "IN ([REDACTED])");

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
        if (upper.Contains("VALUES", StringComparison.Ordinal))
        {
            sql = ValuesClauseRegex().Replace(sql, "VALUES ([REDACTED])");
        }

        // Mask SET clause values in UPDATE statements
        if (upper.Contains("SET", StringComparison.Ordinal))
        {
            sql = SetClauseRegex().Replace(sql, "[column]=[REDACTED]");
        }

        return sql.Trim();
    }

    /// <summary>
    /// Masks sensitive column names in SQL statements.
    /// </summary>
    private static string MaskSensitiveColumns(string sql)
    {
        sql = PasswordRegex().Replace(sql, "[SENSITIVE_COLUMN]");
        sql = SecretRegex().Replace(sql, "[SENSITIVE_COLUMN]");
        sql = TokenRegex().Replace(sql, "[SENSITIVE_COLUMN]");
        sql = ApiKeyRegex().Replace(sql, "[SENSITIVE_COLUMN]");
        sql = PrivateKeyRegex().Replace(sql, "[SENSITIVE_COLUMN]");
        sql = ConnectionStringRegex().Replace(sql, "[SENSITIVE_COLUMN]");
        sql = CreditCardRegex().Replace(sql, "[SENSITIVE_COLUMN]");
        sql = SsnRegex().Replace(sql, "[SENSITIVE_COLUMN]");
        sql = SocialSecurityRegex().Replace(sql, "[SENSITIVE_COLUMN]");
 
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

        if (upper.StartsWith("SELECT", StringComparison.Ordinal))
            return "SELECT operation";
        if (upper.StartsWith("INSERT", StringComparison.Ordinal))
            return "INSERT operation";
        if (upper.StartsWith("UPDATE", StringComparison.Ordinal))
            return "UPDATE operation";
        if (upper.StartsWith("DELETE", StringComparison.Ordinal))
            return "DELETE operation";
        if (upper.StartsWith("CREATE", StringComparison.Ordinal))
            return "CREATE operation";
        if (upper.StartsWith("DROP", StringComparison.Ordinal))
            return "DROP operation";
        if (upper.StartsWith("ALTER", StringComparison.Ordinal))
            return "ALTER operation";

        return "SQL operation";
    }
 
    [GeneratedRegex(@"'[^']*'")]
    private static partial Regex StringLiteralRegex();
 
    [GeneratedRegex(@"(?<=[=<>])\s*\d+")]
    private static partial Regex ComparisonNumericRegex();
 
    [GeneratedRegex(@"\bIN\s*\(\s*\d+[^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex InClauseNumericRegex();
 
    [GeneratedRegex(@"VALUES\s*\([^)]+\)", RegexOptions.IgnoreCase)]
    private static partial Regex ValuesClauseRegex();
 
    [GeneratedRegex(@"SET\s+[^=]+=\s*[^,]+", RegexOptions.IgnoreCase)]
    private static partial Regex SetClauseRegex();
 
    [GeneratedRegex(@"\bpassword\b", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordRegex();
    
    [GeneratedRegex(@"\bsecret\b", RegexOptions.IgnoreCase)]
    private static partial Regex SecretRegex();
    
    [GeneratedRegex(@"\btoken\b", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();
    
    [GeneratedRegex(@"\bapi[_-]?key\b", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKeyRegex();
    
    [GeneratedRegex(@"\bprivate[_-]?key\b", RegexOptions.IgnoreCase)]
    private static partial Regex PrivateKeyRegex();
    
    [GeneratedRegex(@"\bconnection[_-]?string\b", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringRegex();
    
    [GeneratedRegex(@"\bcredit[_-]?card\b", RegexOptions.IgnoreCase)]
    private static partial Regex CreditCardRegex();
    
    [GeneratedRegex(@"\bssn\b", RegexOptions.IgnoreCase)]
    private static partial Regex SsnRegex();
    
    [GeneratedRegex(@"\bsocial[_-]?security\b", RegexOptions.IgnoreCase)]
    private static partial Regex SocialSecurityRegex();
}
