using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;

namespace Redball.Tests
{
    [TestClass]
    public class ProcessWatcherServiceTests
    {
        [TestInitialize]
        public void TestInitialize()
        {
            ProcessWatcherService.Instance.Stop();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            ProcessWatcherService.Instance.Stop();
        }

        [TestMethod]
        public void ProcessWatcherService_Singleton_ReturnsSameInstance()
        {
            var instance1 = ProcessWatcherService.Instance;
            var instance2 = ProcessWatcherService.Instance;

            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void ProcessWatcherService_Start_SetsEnabledAndTarget()
        {
            var service = ProcessWatcherService.Instance;

            service.Start("notepad.exe");

            Assert.IsTrue(service.IsEnabled);
            Assert.AreEqual("notepad", service.TargetProcessName);
        }

        [TestMethod]
        public void ProcessWatcherService_Stop_DisablesService()
        {
            var service = ProcessWatcherService.Instance;
            service.Start("test.exe");

            service.Stop();

            Assert.IsFalse(service.IsEnabled);
            Assert.AreEqual("", service.TargetProcessName);
        }

        [TestMethod]
        public void ProcessWatcherService_EmptyTarget_DoesNotEnable()
        {
            var service = ProcessWatcherService.Instance;

            service.Start("");

            Assert.IsFalse(service.IsEnabled);
        }

        [TestMethod]
        public void ProcessWatcherService_IsTargetRunning_ReturnsBoolean()
        {
            var service = ProcessWatcherService.Instance;

            var result = service.IsTargetRunning;

            Assert.IsInstanceOfType(result, typeof(bool));
        }
    }
}
