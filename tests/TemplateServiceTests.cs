using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Services;
using System;
using System.IO;

namespace Redball.Tests
{
    [TestClass]
    public class TemplateServiceTests
    {
        private string _tempDir = "";

        [TestInitialize]
        public void TestInitialize()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"template_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        [TestMethod]
        public void TemplateService_Singleton_ReturnsSameInstance()
        {
            var instance1 = TemplateService.Instance;
            var instance2 = TemplateService.Instance;

            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void TemplateService_SaveTemplate_CanRetrieve()
        {
            var service = TemplateService.Instance;
            var name = "TestTemplate";
            var content = "Hello, World!";

            var saved = service.SaveTemplate(name, content);

            Assert.IsTrue(saved);
            Assert.AreEqual(content, service.GetTemplate(name));
        }

        [TestMethod]
        public void TemplateService_GetTemplate_NonExistent_ReturnsEmpty()
        {
            var service = TemplateService.Instance;

            var result = service.GetTemplate("NonExistentTemplate12345");

            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void TemplateService_DeleteTemplate_RemovesTemplate()
        {
            var service = TemplateService.Instance;
            var name = "DeleteTest";
            service.SaveTemplate(name, "content");

            var deleted = service.DeleteTemplate(name);

            Assert.IsTrue(deleted);
            Assert.AreEqual(string.Empty, service.GetTemplate(name));
        }

        [TestMethod]
        public void TemplateService_GetTemplateNames_ReturnsSavedTemplates()
        {
            var service = TemplateService.Instance;
            var name1 = $"Test1_{Guid.NewGuid()}";
            var name2 = $"Test2_{Guid.NewGuid()}";

            service.SaveTemplate(name1, "content1");
            service.SaveTemplate(name2, "content2");

            var names = service.GetTemplateNames();

            Assert.IsTrue(names.Contains(name1));
            Assert.IsTrue(names.Contains(name2));

            // Cleanup
            service.DeleteTemplate(name1);
            service.DeleteTemplate(name2);
        }
    }
}
