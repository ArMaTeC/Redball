using System.Text.Json;
using System.Text.Json.Serialization;

namespace Redball.Core.Serialization;

/// <summary>
/// Provides centralized, cached JsonSerializerOptions to comply with CA1869.
/// </summary>
public static class JsonDefaults
{
    private static readonly JsonSerializerOptions _caseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions _indentedCaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions _camelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static JsonSerializerOptions CaseInsensitive => _caseInsensitive;
    public static JsonSerializerOptions IndentedCaseInsensitive => _indentedCaseInsensitive;
    public static JsonSerializerOptions CamelCase => _camelCase;
}
