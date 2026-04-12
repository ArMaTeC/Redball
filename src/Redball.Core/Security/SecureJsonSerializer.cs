namespace Redball.Core.Security;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Secure JSON serializer with strict settings to prevent deserialization attacks.
/// All JSON deserialization should use this class instead of direct JsonSerializer calls.
/// </summary>
public static class SecureJsonSerializer
{
    // Maximum depth to prevent stack overflow from deeply nested JSON
    private const int MaxDepth = 32;

    // Maximum string length to prevent memory exhaustion
    private const int MaxStringLength = 10 * 1024 * 1024; // 10MB

    /// <summary>
    /// Strict serializer options with security-focused settings.
    /// </summary>
    public static readonly JsonSerializerOptions StrictOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = null, // Use exact property names
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = MaxDepth,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false
    };

    /// <summary>
    /// Lenient options for controlled scenarios (internal use only).
    /// </summary>
    internal static readonly JsonSerializerOptions LenientOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        MaxDepth = MaxDepth,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Deserializes JSON with strict security settings.
    /// </summary>
    /// <typeparam name="T">The target type (must be a class).</typeparam>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>The deserialized object, or null if deserialization fails.</returns>
    /// <exception cref="ArgumentException">Thrown when JSON exceeds size limits.</exception>
    public static T? Deserialize<T>(string json) where T : class
    {
        ValidateJsonSize(json);

        try
        {
            return JsonSerializer.Deserialize<T>(json, StrictOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deserializes JSON with strict security settings and returns success status.
    /// </summary>
    /// <typeparam name="T">The target type (must be a class).</typeparam>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <param name="result">The deserialized object if successful.</param>
    /// <returns>True if deserialization succeeded, false otherwise.</returns>
    public static bool TryDeserialize<T>(string json, out T? result) where T : class
    {
        result = null;

        try
        {
            ValidateJsonSize(json);
            result = JsonSerializer.Deserialize<T>(json, StrictOptions);
            return result != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Serializes an object to JSON with strict settings.
    /// </summary>
    /// <typeparam name="T">The type to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The JSON string representation.</returns>
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, StrictOptions);
    }

    private static readonly JsonSerializerOptions PrettyOptions = new(StrictOptions)
    {
        WriteIndented = true
    };
 
    /// <summary>
    /// Serializes an object to JSON with indented formatting for debugging.
    /// </summary>
    /// <typeparam name="T">The type to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The formatted JSON string.</returns>
    public static string SerializePretty<T>(T value)
    {
        return JsonSerializer.Serialize(value, PrettyOptions);
    }

    /// <summary>
    /// Validates that JSON string does not exceed security limits.
    /// </summary>
    /// <param name="json">The JSON to validate.</param>
    /// <exception cref="ArgumentException">Thrown when limits are exceeded.</exception>
    private static void ValidateJsonSize(string json)
    {
        if (string.IsNullOrEmpty(json))
            throw new ArgumentException("JSON cannot be null or empty", nameof(json));

        if (json.Length > MaxStringLength)
            throw new ArgumentException($"JSON exceeds maximum length of {MaxStringLength} characters");
    }

    /// <summary>
    /// Securely deserializes JSON from a stream with size limits.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="stream">The stream containing JSON data.</param>
    /// <param name="maxLength">Maximum allowed length in bytes.</param>
    /// <returns>The deserialized object, or null if deserialization fails.</returns>
    public static T? DeserializeFromStream<T>(Stream stream, long maxLength = MaxStringLength) where T : class
    {
        if (stream.Length > maxLength)
            throw new ArgumentException($"Stream exceeds maximum length of {maxLength} bytes");

        try
        {
            return JsonSerializer.Deserialize<T>(stream, StrictOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
