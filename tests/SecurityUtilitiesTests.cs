using Redball.Core.Security;
using System;
using System.IO;
using System.Security;
using System.Text;
using Xunit;

namespace Redball.Tests;

/// <summary>
/// Unit tests for security utility classes.
/// </summary>
public class SecurityUtilitiesTests
{
    #region SecureJsonSerializer Tests

    public class TestData
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    [Fact]
    public void SecureJsonSerializer_Deserialize_ValidJson_ReturnsObject()
    {
        var json = "{\"Name\":\"test\",\"Value\":42}";
        var result = SecureJsonSerializer.Deserialize<TestData>(json);

        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void SecureJsonSerializer_Deserialize_InvalidJson_ReturnsNull()
    {
        var json = "{invalid json";
        var result = SecureJsonSerializer.Deserialize<TestData>(json);

        Assert.Null(result);
    }

    [Fact]
    public void SecureJsonSerializer_Deserialize_EmptyJson_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => SecureJsonSerializer.Deserialize<TestData>(""));
    }

    [Fact]
    public void SecureJsonSerializer_Deserialize_NullJson_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => SecureJsonSerializer.Deserialize<TestData>(null!));
    }

    [Fact]
    public void SecureJsonSerializer_TryDeserialize_ValidJson_ReturnsTrue()
    {
        var json = "{\"Name\":\"test\",\"Value\":42}";
        var success = SecureJsonSerializer.TryDeserialize<TestData>(json, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void SecureJsonSerializer_TryDeserialize_InvalidJson_ReturnsFalse()
    {
        var json = "{invalid json";
        var success = SecureJsonSerializer.TryDeserialize<TestData>(json, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void SecureJsonSerializer_Serialize_ReturnsValidJson()
    {
        var data = new TestData { Name = "test", Value = 42 };
        var json = SecureJsonSerializer.Serialize(data);

        Assert.Contains("\"Name\":\"test\"", json);
        Assert.Contains("\"Value\":42", json);
    }

    [Fact]
    public void SecureJsonSerializer_SerializePretty_ReturnsIndentedJson()
    {
        var data = new TestData { Name = "test", Value = 42 };
        var json = SecureJsonSerializer.SerializePretty(data);

        Assert.Contains("\n", json);
        Assert.Contains("  ", json);
    }

    [Fact]
    public void SecureJsonSerializer_Deserialize_LargeJson_ThrowsArgumentException()
    {
        var largeJson = new string('a', 11 * 1024 * 1024); // > 10MB
        Assert.Throws<ArgumentException>(() => SecureJsonSerializer.Deserialize<TestData>("{\"data\":\"" + largeJson + "\"}"));
    }

    #endregion

    #region SecurePathValidator Tests

    [Fact]
    public void SecurePathValidator_IsWithinDirectory_ValidPath_ReturnsTrue()
    {
        var baseDir = Path.GetTempPath();
        var path = Path.Combine(baseDir, "test.txt");

        Assert.True(SecurePathValidator.IsWithinDirectory(path, baseDir));
    }

    [Fact]
    public void SecurePathValidator_IsWithinDirectory_PathTraversal_ReturnsFalse()
    {
        var baseDir = Path.GetTempPath();
        var path = Path.Combine(baseDir, "..", "outside.txt");

        Assert.False(SecurePathValidator.IsWithinDirectory(path, baseDir));
    }

    [Fact]
    public void SecurePathValidator_IsWithinDirectory_EmptyPath_ReturnsFalse()
    {
        Assert.False(SecurePathValidator.IsWithinDirectory("", Path.GetTempPath()));
    }

    [Fact]
    public void SecurePathValidator_ValidateNoTraversal_PathTraversal_ThrowsSecurityException()
    {
        var baseDir = Path.GetTempPath();
        var path = Path.Combine(baseDir, "..", "outside.txt");

        Assert.Throws<SecurityException>(() => SecurePathValidator.ValidateNoTraversal(path, baseDir));
    }

    [Fact]
    public void SecurePathValidator_ContainsNoTraversal_NoTraversal_ReturnsTrue()
    {
        Assert.True(SecurePathValidator.ContainsNoTraversal("test.txt"));
    }

    [Fact]
    public void SecurePathValidator_ContainsNoTraversal_WithTraversal_ReturnsFalse()
    {
        Assert.False(SecurePathValidator.ContainsNoTraversal("../test.txt"));
    }

    [Fact]
    public void SecurePathValidator_ContainsNoTraversal_WithTilde_ReturnsFalse()
    {
        Assert.False(SecurePathValidator.ContainsNoTraversal("~/test.txt"));
    }

    [Fact]
    public void SecurePathValidator_SanitiseFileName_ValidName_ReturnsUnchanged()
    {
        var result = SecurePathValidator.SanitiseFileName("test.txt");
        Assert.Equal("test.txt", result);
    }

    [Fact]
    public void SecurePathValidator_SanitiseFileName_InvalidChars_Replaced()
    {
        var result = SecurePathValidator.SanitiseFileName("test<file>.txt");
        Assert.Equal("test_file_.txt", result);
    }

    [Fact]
    public void SecurePathValidator_SanitiseFileName_TraversalSequences_Replaced()
    {
        var result = SecurePathValidator.SanitiseFileName("..test.txt");
        Assert.Equal("_test.txt", result);
    }

    [Fact]
    public void SecurePathValidator_SanitiseFileName_EmptyName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => SecurePathValidator.SanitiseFileName(""));
    }

    [Fact]
    public void SecurePathValidator_HasAllowedExtension_AllowedExtension_ReturnsTrue()
    {
        Assert.True(SecurePathValidator.HasAllowedExtension("test.txt", new[] { ".txt", ".json" }));
    }

    [Fact]
    public void SecurePathValidator_HasAllowedExtension_DisallowedExtension_ReturnsFalse()
    {
        Assert.False(SecurePathValidator.HasAllowedExtension("test.exe", new[] { ".txt", ".json" }));
    }

    [Fact]
    public void SecurePathValidator_HasAllowedExtension_NoExtension_ReturnsFalse()
    {
        Assert.False(SecurePathValidator.HasAllowedExtension("test", new[] { ".txt" }));
    }

    [Fact]
    public void SecurePathValidator_CreateValidatedPath_ValidPath_ReturnsPath()
    {
        var baseDir = Path.GetTempPath();
        var result = SecurePathValidator.CreateValidatedPath(baseDir, "subdir/test.txt");

        Assert.Contains("subdir", result);
        Assert.Contains("test.txt", result);
    }

    [Fact]
    public void SecurePathValidator_CreateValidatedPath_AbsoluteRelativePath_ThrowsSecurityException()
    {
        var baseDir = Path.GetTempPath();

        Assert.Throws<SecurityException>(() => SecurePathValidator.CreateValidatedPath(baseDir, "C:\\test.txt"));
    }

    [Fact]
    public void SecurePathValidator_CreateValidatedPath_PathTraversal_ThrowsSecurityException()
    {
        var baseDir = Path.GetTempPath();

        Assert.Throws<SecurityException>(() => SecurePathValidator.CreateValidatedPath(baseDir, "../test.txt"));
    }

    #endregion

    #region SqlSanitiser Tests

    [Fact]
    public void SqlSanitiser_SanitiseForLogging_StringLiterals_Redacted()
    {
        var sql = "SELECT * FROM Users WHERE Name = 'John Doe'";
        var result = SqlSanitiser.SanitiseForLogging(sql);

        Assert.Contains("'[REDACTED]'", result);
        Assert.DoesNotContain("John Doe", result);
    }

    [Fact]
    public void SqlSanitiser_SanitiseForLogging_NumericValues_Redacted()
    {
        var sql = "SELECT * FROM Users WHERE Age = 25";
        var result = SqlSanitiser.SanitiseForLogging(sql);

        Assert.Contains("[REDACTED]", result);
        Assert.DoesNotContain("25", result);
    }

    [Fact]
    public void SqlSanitiser_SanitiseForLogging_EmptySql_ReturnsEmpty()
    {
        var result = SqlSanitiser.SanitiseForLogging("");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SqlSanitiser_SanitiseForLogging_NullSql_ReturnsEmpty()
    {
        var result = SqlSanitiser.SanitiseForLogging(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SqlSanitiser_MaskInsertUpdateValues_InsertStatement_ValuesRedacted()
    {
        var sql = "INSERT INTO Users (Name, Age) VALUES ('John', 25)";
        var result = SqlSanitiser.MaskInsertUpdateValues(sql);

        Assert.Contains("VALUES ([REDACTED])", result);
    }

    [Fact]
    public void SqlSanitiser_MaskInsertUpdateValues_UpdateStatement_SetClauseRedacted()
    {
        var sql = "UPDATE Users SET Name = 'John'";
        var result = SqlSanitiser.MaskInsertUpdateValues(sql);

        Assert.Contains("[column]=[REDACTED]", result);
    }

    [Fact]
    public void SqlSanitiser_GetOperationDescription_Select_ReturnsSelect()
    {
        var result = SqlSanitiser.GetOperationDescription("SELECT * FROM Users");
        Assert.Equal("SELECT operation", result);
    }

    [Fact]
    public void SqlSanitiser_GetOperationDescription_Insert_ReturnsInsert()
    {
        var result = SqlSanitiser.GetOperationDescription("INSERT INTO Users VALUES (1)");
        Assert.Equal("INSERT operation", result);
    }

    [Fact]
    public void SqlSanitiser_GetOperationDescription_Update_ReturnsUpdate()
    {
        var result = SqlSanitiser.GetOperationDescription("UPDATE Users SET Name='X'");
        Assert.Equal("UPDATE operation", result);
    }

    [Fact]
    public void SqlSanitiser_GetOperationDescription_Delete_ReturnsDelete()
    {
        var result = SqlSanitiser.GetOperationDescription("DELETE FROM Users");
        Assert.Equal("DELETE operation", result);
    }

    [Fact]
    public void SqlSanitiser_GetOperationDescription_Create_ReturnsCreate()
    {
        var result = SqlSanitiser.GetOperationDescription("CREATE TABLE Users");
        Assert.Equal("CREATE operation", result);
    }

    [Fact]
    public void SqlSanitiser_GetOperationDescription_Empty_ReturnsEmpty()
    {
        var result = SqlSanitiser.GetOperationDescription("");
        Assert.Equal("Empty SQL", result);
    }

    [Fact]
    public void SqlSanitiser_SanitiseForLogging_SensitiveColumns_Masked()
    {
        var sql = "SELECT password FROM Users";
        var result = SqlSanitiser.SanitiseForLogging(sql);

        Assert.Contains("[SENSITIVE_COLUMN]", result);
    }

    #endregion

    #region SafeExceptionHandler Tests

    [Fact]
    public void SafeExceptionHandler_Handle_ReturnsSafeMessage()
    {
        var ex = new Exception("Sensitive internal details");
        var result = SafeExceptionHandler.Handle(ex, "TestContext");

        Assert.DoesNotContain("Sensitive internal details", result);
        Assert.Contains("error occurred", result);
    }

    [Fact]
    public void SafeExceptionHandler_Handle_CustomMessage_ReturnsCustomMessage()
    {
        var ex = new Exception("Details");
        var result = SafeExceptionHandler.Handle(ex, "TestContext", userMessage: "Custom error");

        Assert.Equal("Custom error", result);
    }

    [Fact]
    public void SafeExceptionHandler_GetSafeErrorMessage_Default_ReturnsGeneric()
    {
        var result = SafeExceptionHandler.GetSafeErrorMessage();
        Assert.Equal("Operation failed", result);
    }

    [Fact]
    public void SafeExceptionHandler_GetSafeErrorMessage_Custom_ReturnsCustom()
    {
        var result = SafeExceptionHandler.GetSafeErrorMessage("Custom message");
        Assert.Equal("Custom message", result);
    }

    [Fact]
    public void SafeExceptionHandler_GetSafeErrorMessageForOperation_ReturnsFormattedMessage()
    {
        var result = SafeExceptionHandler.GetSafeErrorMessageForOperation("Database update");
        Assert.Equal("Database update failed. Please try again later.", result);
    }

    [Fact]
    public void SafeExceptionHandler_SanitiseErrorMessage_PathRemoved()
    {
        var message = "Error at C:\\Users\\Test\\file.txt";
        var result = SafeExceptionHandler.SanitiseErrorMessage(message);

        Assert.DoesNotContain("C:\\Users", result);
        Assert.Contains("[PATH]", result);
    }

    [Fact]
    public void SafeExceptionHandler_SanitiseErrorMessage_ConnectionStringRemoved()
    {
        var message = "Server=myserver;Database=mydb;User Id=user;Password=pass";
        var result = SafeExceptionHandler.SanitiseErrorMessage(message);

        Assert.DoesNotContain("myserver", result);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("pass", result);
    }

    [Fact]
    public void SafeExceptionHandler_SanitiseErrorMessage_StackTraceTruncated()
    {
        var message = "An error occurred at SomeMethod in SomeFile.cs:line 42";
        var result = SafeExceptionHandler.SanitiseErrorMessage(message);

        Assert.Equal("An error occurred", result);
    }

    [Fact]
    public void SafeExceptionHandler_SanitiseErrorMessage_Empty_ReturnsEmpty()
    {
        var result = SafeExceptionHandler.SanitiseErrorMessage("");
        Assert.Equal(string.Empty, result);
    }

    #endregion
}
