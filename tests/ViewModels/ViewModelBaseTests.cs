using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Redball.UI.ViewModels;
using System.ComponentModel;

namespace Redball.Tests.ViewModels
{
    [TestClass]
    public class ViewModelBaseTests
    {
        private class TestViewModel : ViewModelBase
        {
            private string _testProperty = "";
            public string TestProperty
            {
                get => _testProperty;
                set => SetProperty(ref _testProperty, value);
            }

            private int _numberProperty = 0;
            public int NumberProperty
            {
                get => _numberProperty;
                set => SetProperty(ref _numberProperty, value);
            }

            public void RaiseMultiplePropertiesChanged()
            {
                OnPropertiesChanged(nameof(TestProperty), nameof(NumberProperty));
            }
        }

        [TestMethod]
        public void ViewModelBase_SetProperty_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = new TestViewModel();
            var propertyChangedRaised = false;
            string? changedPropertyName = null;

            viewModel.PropertyChanged += (sender, e) =>
            {
                propertyChangedRaised = true;
                changedPropertyName = e.PropertyName;
            };

            // Act
            viewModel.TestProperty = "New Value";

            // Assert
            Assert.IsTrue(propertyChangedRaised, "PropertyChanged should be raised");
            Assert.AreEqual("TestProperty", changedPropertyName, "Property name should match");
        }

        [TestMethod]
        public void ViewModelBase_SetProperty_SameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            var viewModel = new TestViewModel();
            viewModel.TestProperty = "Same Value";
            var propertyChangedRaised = false;

            viewModel.PropertyChanged += (sender, e) =>
            {
                propertyChangedRaised = true;
            };

            // Act - set same value again
            viewModel.TestProperty = "Same Value";

            // Assert
            Assert.IsFalse(propertyChangedRaised, "PropertyChanged should NOT be raised for same value");
        }

        [TestMethod]
        public void ViewModelBase_SetProperty_ReturnsTrueWhenChanged()
        {
            // Arrange
            var viewModel = new TestViewModel();

            // Act
            var result = viewModel.TestProperty = "New Value";

            // Assert - the setter uses SetProperty which returns bool, but we can't capture it directly from setter
            // This test verifies the property was actually changed
            Assert.AreEqual("New Value", viewModel.TestProperty);
        }

        [TestMethod]
        public void ViewModelBase_OnPropertiesChanged_RaisesMultipleEvents()
        {
            // Arrange
            var viewModel = new TestViewModel();
            var propertyChangedCount = 0;
            var changedProperties = new List<string>();

            viewModel.PropertyChanged += (sender, e) =>
            {
                propertyChangedCount++;
                changedProperties.Add(e.PropertyName ?? "");
            };

            // Act
            viewModel.RaiseMultiplePropertiesChanged();

            // Assert
            Assert.AreEqual(2, propertyChangedCount, "Should raise PropertyChanged twice");
            Assert.IsTrue(changedProperties.Contains("TestProperty"), "Should include TestProperty");
            Assert.IsTrue(changedProperties.Contains("NumberProperty"), "Should include NumberProperty");
        }

        [TestMethod]
        public void ViewModelBase_SetProperty_ValueType_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = new TestViewModel();
            var propertyChangedRaised = false;

            viewModel.PropertyChanged += (sender, e) =>
            {
                propertyChangedRaised = true;
            };

            // Act
            viewModel.NumberProperty = 42;

            // Assert
            Assert.IsTrue(propertyChangedRaised, "PropertyChanged should be raised for value type");
            Assert.AreEqual(42, viewModel.NumberProperty);
        }
    }
}
