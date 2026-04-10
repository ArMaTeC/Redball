# Security API Guide for Developers

This guide documents the security utilities and APIs available in the Redball codebase for secure development practices.

## Overview

The Redball security layer provides utilities for:

- **Secure JSON Deserialization** - Prevent deserialization attacks
- **Path Validation** - Prevent path traversal attacks
- **SQL Sanitisation** - Prevent information disclosure in logs
- **Exception Handling** - Prevent information disclosure to users

All security utilities are located in `Redball.Core.Security` namespace.

---

## SecureJsonSerializer

**Purpose**: Safely deserialise JSON with strict security settings to prevent attacks like:

- Stack overflow from deeply nested JSON
- Memory exhaustion from large payloads
- Type confusion attacks
- Unknown property injection

### API Reference

#### `Deserialize<T>(string json) where T : class`

Deserialises JSON with strict security settings.

```csharp
using Redball.Core.Security;

// Example usage
var json = "{\"Name\":\"John\",\"Age\":30}";
var person = SecureJsonSerializer.Deserialize<Person>(json);

if (person == null)
{
    // Deserialization failed (invalid JSON, security violation, etc.)
    return;
}
```

**Security Limits**:

- Maximum depth: 32 levels
- Maximum payload size: 10MB
- Unknown properties: Rejected
- Case sensitivity: Enabled (exact property names required)

#### `TryDeserialize<T>(string json, out T? result) where T : class`

Same as `Deserialize` but returns boolean success indicator.

```csharp
if (SecureJsonSerializer.TryDeserialize<Person>(json, out var person))
{
    // Use person
}
else
{
    // Handle failure
}
```

#### `Serialize<T>(T value)`

Serialises an object to JSON with strict settings.

```csharp
var person = new Person { Name = "John", Age = 30 };
var json = SecureJsonSerializer.Serialize(person);
```

#### `SerializePretty<T>(T value)`

Serialises with indented formatting for debugging.

```csharp
var json = SecureJsonSerializer.SerializePretty(person);
// Output is indented and human-readable
```

#### `DeserializeFromStream<T>(Stream stream, long maxLength = MaxStringLength)`

Deserialises from a stream with size limits.

```csharp
using var stream = File.OpenRead("data.json");
var data = SecureJsonSerializer.DeserializeFromStream<Person>(stream, maxLength: 5 * 1024 * 1024); // 5MB limit
```

### Migration from JsonSerializer

**Before** (insecure):

```csharp
var data = JsonSerializer.Deserialize<Person>(json);  // ❌ No size/depth limits
```

**After** (secure):

```csharp
var data = SecureJsonSerializer.Deserialize<Person>(json);  // ✅ Protected
```

---

## SecurePathValidator

**Purpose**: Prevent path traversal attacks by validating file paths.

### API Reference1

#### `IsWithinDirectory(string path, string baseDir)`

Checks if a path is within a specified base directory.

```csharp
using Redball.Core.Security;

var userPath = Path.Combine(Path.GetTempPath(), "export", "data.txt");
var tempDir = Path.GetTempPath();

if (SecurePathValidator.IsWithinDirectory(userPath, tempDir))
{
    // Safe to use - path is within temp directory
    File.WriteAllText(userPath, data);
}
else
{
    // Reject - potential path traversal attack
    Logger.Warning("Security", "Path traversal attempt detected");
}
```

#### `ValidateNoTraversal(string path, string baseDir, string context = "Path")`

Validates and throws `SecurityException` on traversal attempts.

```csharp
try
{
    SecurePathValidator.ValidateNoTraversal(userPath, baseDir, "Export path");
    // Safe to proceed
}
catch (SecurityException ex)
{
    // Handle security violation
    Logger.Error("Security", ex.Message);
}
```

#### `ContainsNoTraversal(string path)`

Simple check for traversal sequences without base directory.

```csharp
if (SecurePathValidator.ContainsNoTraversal(filename))
{
    // No ".." or "~" sequences found
}
```

#### `SanitiseFileName(string fileName)`

Sanitises a filename by removing dangerous characters.

```csharp
var userInput = "../../../etc/passwd";
var safeName = SecurePathValidator.SanitiseFileName(userInput);
// Result: "_.._.._.._etc_passwd"

var cleanName = SecurePathValidator.SanitiseFileName("test<file>.txt");
// Result: "test_file_.txt"
```

#### `HasAllowedExtension(string fileName, string[] allowedExtensions)`

Validates file extension against an allow list.

```csharp
var allowed = new[] { ".txt", ".json", ".xml" };

if (SecurePathValidator.HasAllowedExtension(uploadedFile, allowed))
{
    // Extension is allowed
}
```

#### `CreateValidatedPath(string baseDir, string relativePath)`

Creates a combined path guaranteed to be within base directory.

```csharp
try
{
    var fullPath = SecurePathValidator.CreateValidatedPath(
        baseDir: "/app/data",
        relativePath: "users/profile.json");
    
    // fullPath is guaranteed to be within /app/data
}
catch (SecurityException ex)
{
    // Path would escape base directory
}
```

### Best Practices

**Always validate user-provided paths:**

```csharp
public void ExportData(string destinationPath)
{
    // Validate before using
    if (!SecurePathValidator.IsValidFilePath(destinationPath))
    {
        Logger.Error("Export", "Invalid destination path");
        return false;
    }
    
    // Proceed with export
    File.WriteAllText(destinationPath, data);
}
```

**Use `IsValidFilePath` for simple validation:**

```csharp
// Helper method that checks for common traversal patterns
public static bool IsValidFilePath(string path)
{
    return !string.IsNullOrWhiteSpace(path) &&
           SecurePathValidator.ContainsNoTraversal(path);
}
```

---

## SqlSanitiser

**Purpose**: Prevent information disclosure in logs by sanitising SQL statements.

### API Reference2

#### `SanitiseForLogging(string sql)`

Sanitises SQL by removing parameter values.

```csharp
using Redball.Core.Security;

var sql = "SELECT * FROM Users WHERE Name = 'John Doe' AND Age = 25";
var safeLog = SqlSanitiser.SanitiseForLogging(sql);
// Result: "SELECT * FROM Users WHERE Name = '[REDACTED]' AND Age = [REDACTED]"

Logger.Info("Database", safeLog);  // Safe to log - no PII exposed
```

**Sanitisation includes:**

- String literals replaced with `'[REDACTED]'`
- Numeric values replaced with `[REDACTED]`
- IN clause values replaced with `([REDACTED])`
- Sensitive column names replaced with `[SENSITIVE_COLUMN]`

#### `MaskInsertUpdateValues(string sql)`

Masks VALUES and SET clause values.

```csharp
var insert = "INSERT INTO Users (Name, Password) VALUES ('John', 'secret123')";
var masked = SqlSanitiser.MaskInsertUpdateValues(insert);
// Result: "INSERT INTO Users (Name, Password) VALUES ([REDACTED])"

var update = "UPDATE Users SET Password = 'newpass' WHERE Id = 1";
var masked = SqlSanitiser.MaskInsertUpdateValues(update);
// Result: "UPDATE Users [column]=[REDACTED] WHERE Id = [REDACTED]"
```

#### `GetOperationDescription(string sql)`

Returns generic operation type without query details.

```csharp
var desc = SqlSanitiser.GetOperationDescription("SELECT * FROM Users");
// Result: "SELECT operation"

// Use when even sanitised SQL is too detailed
Logger.Info("Database", SqlSanitiser.GetOperationDescription(sql));
```

### Best Practices3

**Always sanitise before logging:**

```csharp
public void ExecuteQuery(string sql)
{
    // Execute the actual query
    _connection.Execute(sql);
    
    // Log sanitised version only
    Logger.Info("Database", SqlSanitiser.SanitiseForLogging(sql));
}
```

**For highly sensitive operations:**

```csharp
// Use generic description only
Logger.Info("Database", SqlSanitiser.GetOperationDescription(sql));
```

---

## SafeExceptionHandler

**Purpose**: Prevent information disclosure in error messages while maintaining internal logging.

### API Reference

#### `Handle(Exception ex, string context, Action<string, string, Exception>? logger = null, string? userMessage = null)`

Handles exceptions securely - logs full details, returns safe message.

```csharp
using Redball.Core.Security;

try
{
    RiskyOperation();
}
catch (Exception ex)
{
    // Log full details internally
    // Return safe message to user
    var userMessage = SafeExceptionHandler.Handle(
        ex,
        context: "PaymentProcessing",
        logger: Logger.Error,
        userMessage: "Payment could not be processed. Please try again."
    );
    
    ShowToUser(userMessage);
}
```

#### `GetSafeErrorMessage(string? customMessage = null)`

Returns generic error message.

```csharp
var message = SafeExceptionHandler.GetSafeErrorMessage();
// Result: "Operation failed"

var custom = SafeExceptionHandler.GetSafeErrorMessage("File upload failed");
// Result: "File upload failed"
```

#### `GetSafeErrorMessageForOperation(string operation)`

Returns formatted message for specific operation.

```csharp
var message = SafeExceptionHandler.GetSafeErrorMessageForOperation("Database update");
// Result: "Database update failed. Please try again later."
```

#### `SanitiseErrorMessage(string message)`

Removes sensitive information from error messages.

```csharp
var error = "Could not connect to Server=mydbserver;Database=production;User=admin";
var safe = SafeExceptionHandler.SanitiseErrorMessage(error);
// Result: "Could not connect to [SERVER];[DATABASE];[CREDENTIALS]"
```

**Sanitisation includes:**

- File paths → `[PATH]`
- Server names → `[SERVER]`
- Database names → `[DATABASE]`
- Credentials → `[CREDENTIALS]`
- Stack traces → truncated to first line only

### Best Practices

**Never expose internal details to users:**

```csharp
// ❌ BAD - Exposes internal details
catch (Exception ex)
{
    return ex.ToString();  // User sees stack trace, paths, etc.
}

// ✅ GOOD - Safe error message
catch (Exception ex)
{
    Logger.Error("Operation", "Processing failed", ex);  // Log internally
    return "Operation failed. Please contact support.";   // Safe user message
}
```

**Use the handler for consistent error handling:**

```csharp
catch (Exception ex)
{
    return SafeExceptionHandler.Handle(ex, "OperationName", Logger.Error);
}
```

---

## Security Checklist for Developers

When implementing new features, ensure:

- [ ] All JSON deserialization uses `SecureJsonSerializer`
- [ ] All file paths from user input are validated with `SecurePathValidator`
- [ ] All SQL statements logged use `SqlSanitiser`
- [ ] All exceptions shown to users use `SafeExceptionHandler`
- [ ] No stack traces are exposed in user-facing messages
- [ ] No file paths are exposed in user-facing messages
- [ ] No connection strings are exposed in logs or messages

---

## Testing Security Utilities

Unit tests are provided in `/root/Redball/tests/SecurityUtilitiesTests.cs`.

Run tests with:

```bash
dotnet test --filter "FullyQualifiedName~SecurityUtilitiesTests"
```

### Key Test Categories

1. **SecureJsonSerializerTests** - JSON size limits, depth limits, invalid input handling
2. **SecurePathValidatorTests** - Path traversal detection, filename sanitisation, extension validation
3. **SqlSanitiserTests** - Parameter redaction, sensitive column masking, operation descriptions
4. **SafeExceptionHandlerTests** - Message sanitisation, safe error messages

---

## Additional Resources

- **Security Justifications**: `docs/SECURITY_JUSTIFICATIONS.md`
- **Security Roadmap**: `docs/SECURITY_SCAN_ROADMAP.md`
- **Source Code**: `src/Redball.Core/Security/`
