using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.Collections.Generic;

namespace Redball.Tests
{
    [TestClass]
    public class TextToSpeechServiceTests
    {
        [TestMethod]
        public void TextToSpeechService_Singleton_ReturnsSameInstance()
        {
            var instance1 = TextToSpeechService.Instance;
            var instance2 = TextToSpeechService.Instance;

            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void TextToSpeechService_IsEnabled_PropertyWorks()
        {
            var service = TextToSpeechService.Instance;

            // Just verify it doesn't throw when getting/setting
            var original = service.IsEnabled;
            service.IsEnabled = !original;
            
            Assert.IsInstanceOfType(service.IsEnabled, typeof(bool));
            
            // Restore
            service.IsEnabled = original;
        }

        [TestMethod]
        public void TextToSpeechService_SpeakAsync_EmptyText_NoException()
        {
            var service = TextToSpeechService.Instance;

            try
            {
                service.SpeakAsync("");
                // Should not throw
                Assert.IsTrue(true);
            }
            catch (Exception ex)
            {
                Assert.Fail($"SpeakAsync with empty text threw exception: {ex.Message}");
            }
        }

        [TestMethod]
        public void TextToSpeechService_SpeakAsync_ShortText_NoException()
        {
            var service = TextToSpeechService.Instance;

            try
            {
                service.SpeakAsync("Hello");
                // Should not throw
                Assert.IsTrue(true);
            }
            catch (Exception ex)
            {
                Assert.Fail($"SpeakAsync threw exception: {ex.Message}");
            }
        }

        [TestMethod]
        public void TextToSpeechService_Stop_NoException()
        {
            var service = TextToSpeechService.Instance;

            try
            {
                service.Stop();
                Assert.IsTrue(true);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Stop threw exception: {ex.Message}");
            }
        }
    }
}
