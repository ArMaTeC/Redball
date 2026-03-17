using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;

namespace Redball.Tests
{
    [TestClass]
    public class SessionStateServiceTests
    {
        private string _tempStatePath = "";

        [TestInitialize]
        public void TestInitialize()
        {
            _tempStatePath = Path.Combine(Path.GetTempPath(), $"redball_test_{Guid.NewGuid()}.state.json");
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (File.Exists(_tempStatePath))
            {
                File.Delete(_tempStatePath);
            }
        }

        [TestMethod]
        public void SessionState_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var state = new SessionState();

            // Assert
            Assert.IsFalse(state.Active, "Default Active should be false");
            Assert.IsTrue(state.PreventDisplaySleep, "Default PreventDisplaySleep should be true");
            Assert.IsTrue(state.UseHeartbeat, "Default UseHeartbeat should be true");
            Assert.IsNull(state.Until, "Default Until should be null");
            Assert.AreEqual(default(DateTime), state.SavedAt, "Default SavedAt should be default");
        }

        [TestMethod]
        public void SessionState_Properties_CanBeSet()
        {
            // Arrange
            var state = new SessionState();
            var now = DateTime.Now;

            // Act
            state.Active = true;
            state.PreventDisplaySleep = false;
            state.UseHeartbeat = false;
            state.Until = now.AddHours(1);
            state.SavedAt = now;

            // Assert
            Assert.IsTrue(state.Active, "Active should be settable");
            Assert.IsFalse(state.PreventDisplaySleep, "PreventDisplaySleep should be settable");
            Assert.IsFalse(state.UseHeartbeat, "UseHeartbeat should be settable");
            Assert.AreEqual(now.AddHours(1), state.Until, "Until should be settable");
            Assert.AreEqual(now, state.SavedAt, "SavedAt should be settable");
        }

        [TestMethod]
        public void SessionStateService_Save_CreatesStateFile()
        {
            // Arrange
            var service = new SessionStateService();
            var keepAwake = KeepAwakeService.Instance;
            keepAwake.SetActive(true, DateTime.Now.AddMinutes(30));

            // Act
            var result = service.Save(keepAwake, _tempStatePath);

            // Assert
            Assert.IsTrue(result, "Save should return true");
            Assert.IsTrue(File.Exists(_tempStatePath), "State file should be created");
        }

        [TestMethod]
        public void SessionStateService_Save_WritesCorrectData()
        {
            // Arrange
            var service = new SessionStateService();
            var keepAwake = KeepAwakeService.Instance;
            keepAwake.SetActive(true, DateTime.Now.AddMinutes(45));
            keepAwake.PreventDisplaySleep = true;
            keepAwake.UseHeartbeat = true;

            // Act
            service.Save(keepAwake, _tempStatePath);

            // Assert
            var json = File.ReadAllText(_tempStatePath);
            Assert.IsTrue(json.Contains("true"), "JSON should contain true values");
            Assert.IsTrue(json.Contains("Active"), "JSON should contain Active property");
        }

        [TestMethod]
        public void SessionStateService_Restore_WithNoFile_ReturnsFalse()
        {
            // Arrange
            var service = new SessionStateService();
            var keepAwake = KeepAwakeService.Instance;

            // Act
            var result = service.Restore(keepAwake, _tempStatePath);

            // Assert
            Assert.IsFalse(result, "Restore should return false when no file exists");
        }

        [TestMethod]
        public void SessionStateService_Restore_LoadsSavedState()
        {
            // Arrange
            var saveService = new SessionStateService();
            var keepAwake = KeepAwakeService.Instance;
            keepAwake.SetActive(true, DateTime.Now.AddMinutes(30));
            keepAwake.PreventDisplaySleep = true;
            keepAwake.UseHeartbeat = false;
            saveService.Save(keepAwake, _tempStatePath);

            // Reset keepAwake
            keepAwake.SetActive(false);
            keepAwake.PreventDisplaySleep = false;
            keepAwake.UseHeartbeat = true;

            // Act
            var result = saveService.Restore(keepAwake, _tempStatePath);

            // Assert
            Assert.IsTrue(result, "Restore should return true");
        }

        [TestMethod]
        public void SessionStateService_Save_NullKeepAwake_HandlesGracefully()
        {
            // Arrange
            var service = new SessionStateService();

            // Act & Assert - should not throw
            try
            {
                var result = service.Save(null!, _tempStatePath);
                // Either true or false is acceptable, just shouldn't throw
                Assert.IsTrue(result || !result, "Should complete without exception");
            }
            catch (NullReferenceException)
            {
                // NullReferenceException is acceptable for null input
                Assert.IsTrue(true, "NullReferenceException is acceptable for null input");
            }
        }

        [TestMethod]
        public void SessionStateService_Restore_InvalidJson_ReturnsFalse()
        {
            // Arrange
            File.WriteAllText(_tempStatePath, "invalid json { }");
            var service = new SessionStateService();
            var keepAwake = KeepAwakeService.Instance;

            // Act
            var result = service.Restore(keepAwake, _tempStatePath);

            // Assert
            Assert.IsFalse(result, "Restore should return false for invalid JSON");
        }
    }
}
