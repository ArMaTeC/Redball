using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.ViewModels;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Redball.Tests.ViewModels
{
    [TestClass]
    public class ValidatableViewModelTests
    {
        // Concrete test implementation with validation rules
        private class TestValidatableVM : ValidatableViewModel
        {
            public string Name
            {
                get => GetProperty<string>();
                set => SetProperty(value);
            }

            public int Age
            {
                get => GetProperty<int>();
                set => SetProperty(value);
            }

            public string Email
            {
                get => GetProperty<string>();
                set => SetProperty(value);
            }

            protected override void ValidatePropertyInternal<T>(string propertyName, T value, List<string> errors)
            {
                switch (propertyName)
                {
                    case nameof(Name):
                        if (value is string name && string.IsNullOrWhiteSpace(name))
                            errors.Add("Name is required");
                        break;
                    case nameof(Age):
                        if (value is int age && (age < 0 || age > 150))
                            errors.Add("Age must be between 0 and 150");
                        break;
                    case nameof(Email):
                        if (value is string email && !string.IsNullOrEmpty(email) && !email.Contains('@'))
                            errors.Add("Email must contain @");
                        break;
                }
            }
        }

        [TestMethod]
        public void SetProperty_ValidValue_NoErrors()
        {
            var vm = new TestValidatableVM();
            vm.Name = "Alice";
            Assert.IsFalse(vm.HasErrors, "Valid name should produce no errors");
        }

        [TestMethod]
        public void SetProperty_InvalidValue_HasErrors()
        {
            var vm = new TestValidatableVM();
            vm.Name = "   ";
            Assert.IsTrue(vm.HasErrors, "Whitespace name should produce errors");
        }

        [TestMethod]
        public void SetProperty_RaisesPropertyChanged()
        {
            var vm = new TestValidatableVM();
            string? changedProp = null;
            vm.PropertyChanged += (s, e) => changedProp = e.PropertyName;

            vm.Name = "Bob";
            Assert.AreEqual("Name", changedProp);
        }

        [TestMethod]
        public void SetProperty_SameValue_ReturnsFalse_NoEvent()
        {
            var vm = new TestValidatableVM();
            vm.Name = "Same";

            var raised = false;
            vm.PropertyChanged += (s, e) => raised = true;

            vm.Name = "Same";
            Assert.IsFalse(raised, "Setting same value should not raise PropertyChanged");
        }

        [TestMethod]
        public void SetProperty_RaisesErrorsChanged()
        {
            var vm = new TestValidatableVM();
            string? errorProp = null;
            vm.ErrorsChanged += (s, e) => errorProp = e.PropertyName;

            vm.Name = "";
            Assert.AreEqual("Name", errorProp, "ErrorsChanged should fire for the validated property");
        }

        [TestMethod]
        public void GetErrors_ForProperty_ReturnsPropertyErrors()
        {
            var vm = new TestValidatableVM();
            vm.Name = "";

            var errors = vm.GetErrors("Name").Cast<string>().ToList();
            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual("Name is required", errors[0]);
        }

        [TestMethod]
        public void GetErrors_ForValidProperty_ReturnsEmpty()
        {
            var vm = new TestValidatableVM();
            vm.Name = "Valid";

            var errors = vm.GetErrors("Name").Cast<string>().ToList();
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void GetErrors_NullPropertyName_ReturnsAllErrors()
        {
            var vm = new TestValidatableVM();
            vm.Name = "";       // invalid
            vm.Age = -1;        // invalid

            var allErrors = vm.GetErrors(null).Cast<string>().ToList();
            Assert.IsTrue(allErrors.Count >= 2, $"Should have at least 2 errors, got {allErrors.Count}");
        }

        [TestMethod]
        public void GetErrors_EmptyPropertyName_ReturnsAllErrors()
        {
            var vm = new TestValidatableVM();
            vm.Name = "";
            vm.Age = 200;

            var allErrors = vm.GetErrors("").Cast<string>().ToList();
            Assert.IsTrue(allErrors.Count >= 2);
        }

        [TestMethod]
        public void GetAllErrors_ReturnsDict()
        {
            var vm = new TestValidatableVM();
            vm.Name = "";
            vm.Age = -5;

            var dict = vm.GetAllErrors();
            Assert.IsTrue(dict.ContainsKey("Name"));
            Assert.IsTrue(dict.ContainsKey("Age"));
        }

        [TestMethod]
        public void ClearErrors_RemovesAllErrors()
        {
            var vm = new TestValidatableVM();
            vm.Name = "";
            vm.Age = -1;
            Assert.IsTrue(vm.HasErrors);

            vm.ClearErrors();
            Assert.IsFalse(vm.HasErrors, "ClearErrors should remove all errors");
        }

        [TestMethod]
        public void ClearErrors_RaisesErrorsChanged()
        {
            var vm = new TestValidatableVM();
            vm.Name = "";

            string? clearedProp = null;
            vm.ErrorsChanged += (s, e) => clearedProp = e.PropertyName;

            vm.ClearErrors();
            Assert.IsNull(clearedProp, "ClearErrors should raise ErrorsChanged with null property name");
        }

        [TestMethod]
        public void HasErrors_FalseByDefault()
        {
            var vm = new TestValidatableVM();
            Assert.IsFalse(vm.HasErrors);
        }

        [TestMethod]
        public void FixingInvalidValue_ClearsError()
        {
            var vm = new TestValidatableVM();
            vm.Name = "";
            Assert.IsTrue(vm.HasErrors);

            vm.Name = "Fixed";
            Assert.IsFalse(vm.HasErrors, "Fixing the value should clear the error");
        }

        [TestMethod]
        public void MultipleProperties_IndependentErrors()
        {
            var vm = new TestValidatableVM();
            vm.Name = "";       // invalid
            vm.Age = 25;        // valid
            vm.Email = "bad";   // invalid (no @)

            Assert.IsTrue(vm.HasErrors);
            var nameErrors = vm.GetErrors("Name").Cast<string>().ToList();
            var ageErrors = vm.GetErrors("Age").Cast<string>().ToList();
            var emailErrors = vm.GetErrors("Email").Cast<string>().ToList();

            Assert.AreEqual(1, nameErrors.Count);
            Assert.AreEqual(0, ageErrors.Count);
            Assert.AreEqual(1, emailErrors.Count);
        }

        [TestMethod]
        public void Email_WithAtSign_NoErrors()
        {
            var vm = new TestValidatableVM();
            vm.Email = "test@example.com";
            var errors = vm.GetErrors("Email").Cast<string>().ToList();
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void Age_AtBoundary_NoErrors()
        {
            var vm = new TestValidatableVM();

            vm.Age = 0;
            Assert.IsFalse(vm.GetErrors("Age").Cast<string>().Any(), "Age=0 should be valid");

            vm.Age = 150;
            Assert.IsFalse(vm.GetErrors("Age").Cast<string>().Any(), "Age=150 should be valid");
        }

        [TestMethod]
        public void Age_OutOfBoundary_HasErrors()
        {
            var vm = new TestValidatableVM();

            vm.Age = -1;
            Assert.IsTrue(vm.GetErrors("Age").Cast<string>().Any(), "Age=-1 should be invalid");

            vm.Age = 151;
            Assert.IsTrue(vm.GetErrors("Age").Cast<string>().Any(), "Age=151 should be invalid");
        }
    }

    [TestClass]
    public class ValidationAttributeTests
    {
        [TestMethod]
        public void RequiredAttribute_DefaultMessage()
        {
            var attr = new RequiredAttribute();
            Assert.AreEqual("This field is required", attr.ErrorMessage);
        }

        [TestMethod]
        public void RequiredAttribute_CustomMessage()
        {
            var attr = new RequiredAttribute("Name is mandatory");
            Assert.AreEqual("Name is mandatory", attr.ErrorMessage);
        }

        [TestMethod]
        public void RangeAttribute_StoresMinMax()
        {
            var attr = new RangeAttribute(10, 300);
            Assert.AreEqual(10, attr.Minimum);
            Assert.AreEqual(300, attr.Maximum);
        }

        [TestMethod]
        public void RangeAttribute_FormatsErrorMessage()
        {
            var attr = new RangeAttribute(5, 95);
            Assert.IsTrue(attr.ErrorMessage.Contains("5"), "Error should contain minimum");
            Assert.IsTrue(attr.ErrorMessage.Contains("95"), "Error should contain maximum");
        }

        [TestMethod]
        public void StringLengthAttribute_StoresMaxLength()
        {
            var attr = new StringLengthAttribute(100);
            Assert.AreEqual(100, attr.MaximumLength);
            Assert.AreEqual(0, attr.MinimumLength);
        }

        [TestMethod]
        public void StringLengthAttribute_MinLength_CanBeSet()
        {
            var attr = new StringLengthAttribute(50) { MinimumLength = 5 };
            Assert.AreEqual(5, attr.MinimumLength);
            Assert.AreEqual(50, attr.MaximumLength);
        }

        [TestMethod]
        public void RegularExpressionAttribute_StoresPattern()
        {
            var attr = new RegularExpressionAttribute(@"^\d+$", "Must be digits only");
            Assert.AreEqual(@"^\d+$", attr.Pattern);
            Assert.AreEqual("Must be digits only", attr.ErrorMessage);
        }
    }
}
