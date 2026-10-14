using Redball.Core.Security;
using System;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Redball.Tests;

/// <summary>
/// Unit tests for security utility classes.
/// </summary>
[TestClass]
public class SecurityUtilitiesTests
{
    #region SecureJsonSerializer Tests

    public class TestData
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    [TestMethod]
    public void SecureJsonSerializer_Deserialize_ValidJson_ReturnsObject()
    {
        var json = "{\"Name\":\"test\",\"Value\":42}";
        var result = SecureJsonSerializer.Deserialize<TestData>(json);

        Assert.IsNotNull(result);
        Assert.AreEqual("test", result.Name);
        Assert.AreEqual(42, result.Value);
    }

    [TestMethod]
    public void SecureJsonSerializer_Deserialize_InvalidJson_ReturnsNull()
    {
        var json = "{invalid json";
        var result = SecureJsonSerializer.Deserialize<TestData>(json);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void SecureJsonSerializer_Deserialize_EmptyJson_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => SecureJsonSerializer.Deserialize<TestData>(""));
    }

    [TestMethod]
    public void SecureJsonSerializer_Deserialize_NullJson_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => SecureJsonSerializer.Deserialize<TestData>(null!));
    }

    [TestMethod]
    public void SecureJsonSerializer_TryDeserialize_ValidJson_ReturnsTrue()
    {
        var json = "{\"Name\":\"test\",\"Value\":42}";
        var success = SecureJsonSerializer.TryDeserialize<TestData>(json, out var result);

        Assert.IsTrue(success);
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void SecureJsonSerializer_TryDeserialize_InvalidJson_ReturnsFalse()
    {
        var json = "{invalid json";
        var success = SecureJsonSerializer.TryDeserialize<TestData>(json, out var result);

        Assert.IsFalse(success);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void SecureJsonSerializer_Serialize_ReturnsValidJson()
    {
        var data = new TestData { Name = "test", Value = 42 };
        var json = SecureJsonSerializer.Serialize(data);

        StringAssert.Contains(json, "\"Name\":\"test\"");
        StringAssert.Contains(json, "\"Value\":42");
    }

    [TestMethod]
    public void SecureJsonSerializer_SerializePretty_ReturnsIndentedJson()
    {
        var data = new TestData { Name = "test", Value = 42 };
        var json = SecureJsonSerializer.SerializePretty(data);

        StringAssert.Contains(json, "\n");
        StringAssert.Contains(json, "  ");
    }

    [TestMethod]
    public void SecureJsonSerializer_Deserialize_LargeJson_ThrowsArgumentException()
    {
        var largeJson = new string('a', 11 * 1024 * 1024); // > 10MB
        Assert.ThrowsExactly<ArgumentException>(() => SecureJsonSerializer.Deserialize<TestData>("{\"data\":\"" + largeJson + "\"}"));
    }

    #endregion

    #region SecurePathValidator Tests

    [TestMethod]
    public void SecurePathValidator_IsWithinDirectory_ValidPath_ReturnsTrue()
    {
        var baseDir = Path.GetTempPath();
        var path = Path.Combine(baseDir, "test.txt");

        Assert.IsTrue(SecurePathValidator.IsWithinDirectory(path, baseDir));
    }

    [TestMethod]
    public void SecurePathValidator_IsWithinDirectory_PathTraversal_ReturnsFalse()
    {
        var baseDir = Path.GetTempPath();
        var path = Path.Combine(baseDir, "..", "outside.txt");

        Assert.IsFalse(SecurePathValidator.IsWithinDirectory(path, baseDir));
    }

    [TestMethod]
    public void SecurePathValidator_IsWithinDirectory_EmptyPath_ReturnsFalse()
    {
        Assert.IsFalse(SecurePathValidator.IsWithinDirectory("", Path.GetTempPath()));
    }

    [TestMethod]
    public void SecurePathValidator_ValidateNoTraversal_PathTraversal_ThrowsSecurityException()
    {
        var baseDir = Path.GetTempPath();
        var path = Path.Combine(baseDir, "..", "outside.txt");

        Assert.ThrowsExactly<SecurityException>(() => SecurePathValidator.ValidateNoTraversal(path, baseDir));
    }

    [TestMethod]
    public void SecurePathValidator_ContainsNoTraversal_NoTraversal_ReturnsTrue()
    {
        Assert.IsTrue(SecurePathValidator.ContainsNoTraversal("test.txt"));
    }

    [TestMethod]
    public void SecurePathValidator_ContainsNoTraversal_WithTraversal_ReturnsFalse()
    {
        Assert.IsFalse(SecurePathValidator.ContainsNoTraversal("../test.txt"));
    }

    [TestMethod]
    public void SecurePathValidator_ContainsNoTraversal_WithTilde_ReturnsFalse()
    {
        Assert.IsFalse(SecurePathValidator.ContainsNoTraversal("~/test.txt"));
    }

    [TestMethod]
    public void SecurePathValidator_SanitiseFileName_ValidName_ReturnsUnchanged()
    {
        var result = SecurePathValidator.SanitiseFileName("test.txt");
        Assert.AreEqual("test.txt", result);
    }

    [TestMethod]
    public void SecurePathValidator_SanitiseFileName_InvalidChars_Replaced()
    {
        var result = SecurePathValidator.SanitiseFileName("test<file>.txt");
        Assert.AreEqual("test_file_.txt", result);
    }

    [TestMethod]
    public void SecurePathValidator_SanitiseFileName_TraversalSequences_Replaced()
    {
        var result = SecurePathValidator.SanitiseFileName("..test.txt");
        Assert.AreEqual("_test.txt", result);
    }

    [TestMethod]
    public void SecurePathValidator_SanitiseFileName_EmptyName_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => SecurePathValidator.SanitiseFileName(""));
    }

    [TestMethod]
    public void SecurePathValidator_HasAllowedExtension_AllowedExtension_ReturnsTrue()
    {
        Assert.IsTrue(SecurePathValidator.HasAllowedExtension("test.txt", new[] { ".txt", ".json" }));
    }

    [TestMethod]
    public void SecurePathValidator_HasAllowedExtension_DisallowedExtension_ReturnsFalse()
    {
        Assert.IsFalse(SecurePathValidator.HasAllowedExtension("test.exe", new[] { ".txt", ".json" }));
    }

    [TestMethod]
    public void SecurePathValidator_HasAllowedExtension_NoExtension_ReturnsFalse()
    {
        Assert.IsFalse(SecurePathValidator.HasAllowedExtension("test", new[] { ".txt" }));
    }

    [TestMethod]
    public void SecurePathValidator_CreateValidatedPath_ValidPath_ReturnsPath()
    {
        var baseDir = Path.GetTempPath();
        var result = SecurePathValidator.CreateValidatedPath(baseDir, "subdir/test.txt");

        StringAssert.Contains(result, "subdir");
        StringAssert.Contains(result, "test.txt");
    }

    [TestMethod]
    public void SecurePathValidator_CreateValidatedPath_AbsoluteRelativePath_ThrowsSecurityException()
    {
        var baseDir = Path.GetTempPath();

        Assert.ThrowsExactly<SecurityException>(() => SecurePathValidator.CreateValidatedPath(baseDir, "C:\\test.txt"));
    }

    [TestMethod]
    public void SecurePathValidator_CreateValidatedPath_PathTraversal_ThrowsSecurityException()
    {
        var baseDir = Path.GetTempPath();

        Assert.ThrowsExactly<SecurityException>(() => SecurePathValidator.CreateValidatedPath(baseDir, "../test.txt"));
    }

    #endregion

    #region SqlSanitiser Tests

    [TestMethod]
    public void SqlSanitiser_SanitiseForLogging_StringLiterals_Redacted()
    {
        var sql = "SELECT * FROM Users WHERE Name = 'John Doe'";
        var result = SqlSanitiser.SanitiseForLogging(sql);

        StringAssert.Contains(result, "'[REDACTED]'");
        Assert.IsFalse(result.Contains("John Doe"));
    }

    [TestMethod]
    public void SqlSanitiser_SanitiseForLogging_NumericValues_Redacted()
    {
        var sql = "SELECT * FROM Users WHERE Age = 25";
        var result = SqlSanitiser.SanitiseForLogging(sql);

        StringAssert.Contains(result, "[REDACTED]");
        Assert.IsFalse(result.Contains("25"));
    }

    [TestMethod]
    public void SqlSanitiser_SanitiseForLogging_EmptySql_ReturnsEmpty()
    {
        var result = SqlSanitiser.SanitiseForLogging("");
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void SqlSanitiser_SanitiseForLogging_NullSql_ReturnsEmpty()
    {
        var result = SqlSanitiser.SanitiseForLogging(null);
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void SqlSanitiser_MaskInsertUpdateValues_InsertStatement_ValuesRedacted()
    {
        var sql = "INSERT INTO Users (Name, Age) VALUES ('John', 25)";
        var result = SqlSanitiser.MaskInsertUpdateValues(sql);

        StringAssert.Contains(result, "VALUES ([REDACTED])");
    }

    [TestMethod]
    public void SqlSanitiser_MaskInsertUpdateValues_UpdateStatement_SetClauseRedacted()
    {
        var sql = "UPDATE Users SET Name = 'John'";
        var result = SqlSanitiser.MaskInsertUpdateValues(sql);

        StringAssert.Contains(result, "[column]=[REDACTED]");
    }

    [TestMethod]
    public void SqlSanitiser_GetOperationDescription_Select_ReturnsSelect()
    {
        var result = SqlSanitiser.GetOperationDescription("SELECT * FROM Users");
        Assert.AreEqual("SELECT operation", result);
    }

    [TestMethod]
    public void SqlSanitiser_GetOperationDescription_Insert_ReturnsInsert()
    {
        var result = SqlSanitiser.GetOperationDescription("INSERT INTO Users VALUES (1)");
        Assert.AreEqual("INSERT operation", result);
    }

    [TestMethod]
    public void SqlSanitiser_GetOperationDescription_Update_ReturnsUpdate()
    {
        var result = SqlSanitiser.GetOperationDescription("UPDATE Users SET Name='X'");
        Assert.AreEqual("UPDATE operation", result);
    }

    [TestMethod]
    public void SqlSanitiser_GetOperationDescription_Delete_ReturnsDelete()
    {
        var result = SqlSanitiser.GetOperationDescription("DELETE FROM Users");
        Assert.AreEqual("DELETE operation", result);
    }

    [TestMethod]
    public void SqlSanitiser_GetOperationDescription_Create_ReturnsCreate()
    {
        var result = SqlSanitiser.GetOperationDescription("CREATE TABLE Users");
        Assert.AreEqual("CREATE operation", result);
    }

    [TestMethod]
    public void SqlSanitiser_GetOperationDescription_Empty_ReturnsEmpty()
    {
        var result = SqlSanitiser.GetOperationDescription("");
        Assert.AreEqual("Empty SQL", result);
    }

    [TestMethod]
    public void SqlSanitiser_SanitiseForLogging_SensitiveColumns_Masked()
    {
        var sql = "SELECT password FROM Users";
        var result = SqlSanitiser.SanitiseForLogging(sql);

        StringAssert.Contains(result, "[SENSITIVE_COLUMN]");
    }

    #endregion

    #region SafeExceptionHandler Tests

    [TestMethod]
    public void SafeExceptionHandler_Handle_ReturnsSafeMessage()
    {
        var ex = new Exception("Sensitive internal details");
        var result = SafeExceptionHandler.Handle(ex, "TestContext");

        Assert.IsFalse(result.Contains("Sensitive internal details"));
        StringAssert.Contains(result, "error occurred");
    }

    [TestMethod]
    public void SafeExceptionHandler_Handle_CustomMessage_ReturnsCustomMessage()
    {
        var ex = new Exception("Details");
        var result = SafeExceptionHandler.Handle(ex, "TestContext", userMessage: "Custom error");

        Assert.AreEqual("Custom error", result);
    }

    [TestMethod]
    public void SafeExceptionHandler_GetSafeErrorMessage_Default_ReturnsGeneric()
    {
        var result = SafeExceptionHandler.GetSafeErrorMessage();
        Assert.AreEqual("Operation failed", result);
    }

    [TestMethod]
    public void SafeExceptionHandler_GetSafeErrorMessage_Custom_ReturnsCustom()
    {
        var result = SafeExceptionHandler.GetSafeErrorMessage("Custom message");
        Assert.AreEqual("Custom message", result);
    }

    [TestMethod]
    public void SafeExceptionHandler_GetSafeErrorMessageForOperation_ReturnsFormattedMessage()
    {
        var result = SafeExceptionHandler.GetSafeErrorMessageForOperation("Database update");
        Assert.AreEqual("Database update failed. Please try again later.", result);
    }

    [TestMethod]
    public void SafeExceptionHandler_SanitiseErrorMessage_PathRemoved()
    {
        var message = "Error at C:\\Users\\Test\\file.txt";
        var result = SafeExceptionHandler.SanitiseErrorMessage(message);

        Assert.IsFalse(result.Contains("C:\\Users"));
        StringAssert.Contains(result, "[PATH]");
    }

    [TestMethod]
    public void SafeExceptionHandler_SanitiseErrorMessage_ConnectionStringRemoved()
    {
        var message = "Server=myserver;Database=mydb;User Id=user;Password=pass";
        var result = SafeExceptionHandler.SanitiseErrorMessage(message);

        Assert.IsFalse(result.Contains("myserver"));
        Assert.IsFalse(result.Contains("user"));
        Assert.IsFalse(result.Contains("pass"));
    }

    [TestMethod]
    public void SafeExceptionHandler_SanitiseErrorMessage_StackTraceTruncated()
    {
        var message = "An error occurred at SomeMethod in SomeFile.cs:line 42";
        var result = SafeExceptionHandler.SanitiseErrorMessage(message);

        Assert.AreEqual("An error occurred", result);
    }

    [TestMethod]
    public void SafeExceptionHandler_SanitiseErrorMessage_Empty_ReturnsEmpty()
    {
        var result = SafeExceptionHandler.SanitiseErrorMessage("");
        Assert.AreEqual(string.Empty, result);
    }

    #endregion
}
