using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Redball.UI.ViewModels;

/// <summary>
/// Base ViewModel with validation support and INotifyPropertyChanged
/// </summary>
public abstract class ValidatableViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
{
    private readonly Dictionary<string, List<string>> _errors = new();
    private readonly Dictionary<string, object?> _propertyValues = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <summary>
    /// Gets a value indicating whether the entity has validation errors.
    /// </summary>
    public bool HasErrors => _errors.Count > 0;

    /// <summary>
    /// Gets the validation errors for a specified property or for the entire entity.
    /// </summary>
    public System.Collections.IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            // Return all errors
            foreach (var errors in _errors.Values)
            {
                foreach (var error in errors)
                {
                    yield return error;
                }
            }
        }
        else if (_errors.TryGetValue(propertyName, out var propertyErrors))
        {
            foreach (var error in propertyErrors)
            {
                yield return error;
            }
        }
    }

    /// <summary>
    /// Gets all errors for all properties
    /// </summary>
    public Dictionary<string, List<string>> GetAllErrors()
    {
        return new Dictionary<string, List<string>>(_errors);
    }

    /// <summary>
    /// Gets or sets a property value with validation
    /// </summary>
    protected T GetProperty<T>([CallerMemberName] string? propertyName = null)
    {
        if (propertyName == null) throw new ArgumentNullException(nameof(propertyName));
        
        if (_propertyValues.TryGetValue(propertyName, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        
        return default!;
    }

    /// <summary>
    /// Sets a property value and performs validation
    /// </summary>
    protected bool SetProperty<T>(T value, [CallerMemberName] string? propertyName = null)
    {
        if (propertyName == null) throw new ArgumentNullException(nameof(propertyName));

        var currentValue = GetProperty<T>(propertyName);
        if (EqualityComparer<T>.Default.Equals(currentValue, value))
        {
            return false;
        }

        _propertyValues[propertyName] = value;
        OnPropertyChanged(propertyName);
        ValidateProperty(propertyName, value);
        return true;
    }

    /// <summary>
    /// Validates a specific property
    /// </summary>
    protected void ValidateProperty<T>(string propertyName, T value)
    {
        var errors = new List<string>();
        ValidatePropertyInternal(propertyName, value, errors);

        if (errors.Count > 0)
        {
            _errors[propertyName] = errors;
        }
        else
        {
            _errors.Remove(propertyName);
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Override this method to add custom validation logic
    /// </summary>
    protected virtual void ValidatePropertyInternal<T>(string propertyName, T value, List<string> errors)
    {
        // Override in derived classes
    }

    /// <summary>
    /// Validates all properties
    /// </summary>
    public virtual bool Validate()
    {
        foreach (var propertyName in _propertyValues.Keys)
        {
            var value = _propertyValues[propertyName];
            ValidatePropertyInternal(propertyName, value, new List<string>());
        }
        return !HasErrors;
    }

    /// <summary>
    /// Clears all validation errors
    /// </summary>
    public void ClearErrors()
    {
        _errors.Clear();
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(null));
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Validation attributes for ViewModel properties
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class RequiredAttribute : Attribute
{
    public string ErrorMessage { get; }

    public RequiredAttribute(string errorMessage = "This field is required")
    {
        ErrorMessage = errorMessage;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class RangeAttribute : Attribute
{
    public double Minimum { get; }
    public double Maximum { get; }
    public string ErrorMessage { get; }

    public RangeAttribute(double minimum, double maximum, string errorMessage = "Value must be between {0} and {1}")
    {
        Minimum = minimum;
        Maximum = maximum;
        ErrorMessage = string.Format(errorMessage, minimum, maximum);
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class StringLengthAttribute : Attribute
{
    public int MaximumLength { get; }
    public int MinimumLength { get; set; }
    public string ErrorMessage { get; }

    public StringLengthAttribute(int maximumLength, string errorMessage = "Value must be between {0} and {1} characters")
    {
        MaximumLength = maximumLength;
        ErrorMessage = errorMessage;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class RegularExpressionAttribute : Attribute
{
    public string Pattern { get; }
    public string ErrorMessage { get; }

    public RegularExpressionAttribute(string pattern, string errorMessage = "Invalid format")
    {
        Pattern = pattern;
        ErrorMessage = errorMessage;
    }
}
