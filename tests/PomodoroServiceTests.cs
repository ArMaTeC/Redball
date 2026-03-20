using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;

namespace Redball.Tests
{
    [TestClass]
    public class PomodoroServiceTests
    {
        [TestInitialize]
        public void TestInitialize()
        {
            PomodoroService.Instance.Stop();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            PomodoroService.Instance.Stop();
        }

        [TestMethod]
        public void PomodoroService_Start_SetsPhaseToFocus()
        {
            var service = PomodoroService.Instance;
            service.Start();

            Assert.AreEqual(PomodoroService.PomodoroPhase.Focus, service.CurrentPhase);
            Assert.IsTrue(service.IsRunning);
        }

        [TestMethod]
        public void PomodoroService_Stop_SetsPhaseToIdle()
        {
            var service = PomodoroService.Instance;
            service.Start();
            service.Stop();

            Assert.AreEqual(PomodoroService.PomodoroPhase.Idle, service.CurrentPhase);
            Assert.IsFalse(service.IsRunning);
        }

        [TestMethod]
        public void PomodoroService_Singleton_ReturnsSameInstance()
        {
            var instance1 = PomodoroService.Instance;
            var instance2 = PomodoroService.Instance;

            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void PomodoroService_StateChanged_EventRaised()
        {
            var service = PomodoroService.Instance;
            var eventRaised = false;

            service.StateChanged += (s, e) => eventRaised = true;
            service.Start();

            Assert.IsTrue(eventRaised);
        }

        [TestMethod]
        public void PomodoroService_Remaining_ReturnsTimeSpan()
        {
            var service = PomodoroService.Instance;
            service.Start();

            var remaining = service.Remaining;

            Assert.IsInstanceOfType(remaining, typeof(TimeSpan));
            Assert.IsTrue(remaining.TotalSeconds > 0);
        }
    }
}
