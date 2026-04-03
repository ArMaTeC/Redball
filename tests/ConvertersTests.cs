using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Redball.UI.Converters;

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type - null is valid for WPF converter parameters

namespace Redball.Tests;

/// <summary>
/// Tests for WPF value converters to ensure proper two-way binding support.
/// </summary>
[TestClass]
public class ConvertersTests
{
    #region StatusToBrushConverter Tests

    [TestMethod]
    public void StatusToBrushConverter_Convert_True_ReturnsRedBrush()
    {
        var converter = new StatusToBrushConverter();
        var result = converter.Convert(true, typeof(Brush), null, CultureInfo.InvariantCulture);
        
        Assert.IsInstanceOfType(result, typeof(SolidColorBrush));
        var brush = (SolidColorBrush)result;
        Assert.AreEqual(Color.FromRgb(220, 53, 69), brush.Color);
    }

    [TestMethod]
    public void StatusToBrushConverter_Convert_False_ReturnsGrayBrush()
    {
        var converter = new StatusToBrushConverter();
        var result = converter.Convert(false, typeof(Brush), null, CultureInfo.InvariantCulture);
        
        Assert.IsInstanceOfType(result, typeof(SolidColorBrush));
        var brush = (SolidColorBrush)result;
        Assert.AreEqual(Color.FromRgb(108, 117, 125), brush.Color);
    }

    [TestMethod]
    public void StatusToBrushConverter_ConvertBack_ReturnsDoNothing()
    {
        var converter = new StatusToBrushConverter();
        var brush = new SolidColorBrush(Colors.Red);
        var result = converter.ConvertBack(brush, typeof(bool), null, CultureInfo.InvariantCulture);
        
        // Should return Binding.DoNothing since Brush->bool conversion is not meaningful
        Assert.AreEqual(Binding.DoNothing, result);
    }

    #endregion

    #region BoolToTextConverter Tests

    [TestMethod]
    public void BoolToTextConverter_Convert_True_ReturnsFirstPart()
    {
        var converter = new BoolToTextConverter();
        var result = converter.Convert(true, typeof(string), "Active|Inactive", CultureInfo.InvariantCulture);
        
        Assert.AreEqual("Active", result);
    }

    [TestMethod]
    public void BoolToTextConverter_Convert_False_ReturnsSecondPart()
    {
        var converter = new BoolToTextConverter();
        var result = converter.Convert(false, typeof(string), "Active|Inactive", CultureInfo.InvariantCulture);
        
        Assert.AreEqual("Inactive", result);
    }

    [TestMethod]
    public void BoolToTextConverter_ConvertBack_ActiveText_ReturnsTrue()
    {
        var converter = new BoolToTextConverter();
        var result = converter.ConvertBack("Active", typeof(bool), "Active|Inactive", CultureInfo.InvariantCulture);
        
        Assert.IsInstanceOfType(result, typeof(bool));
        Assert.IsTrue((bool)result);
    }

    [TestMethod]
    public void BoolToTextConverter_ConvertBack_InactiveText_ReturnsFalse()
    {
        var converter = new BoolToTextConverter();
        var result = converter.ConvertBack("Inactive", typeof(bool), "Active|Inactive", CultureInfo.InvariantCulture);
        
        Assert.IsInstanceOfType(result, typeof(bool));
        Assert.IsFalse((bool)result);
    }

    [TestMethod]
    public void BoolToTextConverter_ConvertBack_CaseInsensitive()
    {
        var converter = new BoolToTextConverter();
        var result = converter.ConvertBack("ACTIVE", typeof(bool), "Active|Inactive", CultureInfo.InvariantCulture);
        
        Assert.IsTrue((bool)result);
    }

    [TestMethod]
    public void BoolToTextConverter_ConvertBack_UnknownText_ReturnsUnsetValue()
    {
        var converter = new BoolToTextConverter();
        var result = converter.ConvertBack("Unknown", typeof(bool), "Active|Inactive", CultureInfo.InvariantCulture);
        
        Assert.AreEqual(DependencyProperty.UnsetValue, result);
    }

    [TestMethod]
    public void BoolToTextConverter_ConvertBack_InvalidParameter_ReturnsUnsetValue()
    {
        var converter = new BoolToTextConverter();
        var result = converter.ConvertBack("Active", typeof(bool), "InvalidParameter", CultureInfo.InvariantCulture);
        
        Assert.AreEqual(DependencyProperty.UnsetValue, result);
    }

    #endregion

    #region InverseBoolConverter Tests

    [TestMethod]
    public void InverseBoolConverter_Convert_True_ReturnsFalse()
    {
        var converter = new InverseBoolConverter();
        var result = converter.Convert(true, typeof(bool), null, CultureInfo.InvariantCulture);
        
        Assert.IsFalse((bool)result);
    }

    [TestMethod]
    public void InverseBoolConverter_Convert_False_ReturnsTrue()
    {
        var converter = new InverseBoolConverter();
        var result = converter.Convert(false, typeof(bool), null, CultureInfo.InvariantCulture);
        
        Assert.IsTrue((bool)result);
    }

    [TestMethod]
    public void InverseBoolConverter_ConvertBack_True_ReturnsFalse()
    {
        var converter = new InverseBoolConverter();
        var result = converter.ConvertBack(true, typeof(bool), null, CultureInfo.InvariantCulture);
        
        Assert.IsFalse((bool)result);
    }

    [TestMethod]
    public void InverseBoolConverter_ConvertBack_False_ReturnsTrue()
    {
        var converter = new InverseBoolConverter();
        var result = converter.ConvertBack(false, typeof(bool), null, CultureInfo.InvariantCulture);
        
        Assert.IsTrue((bool)result);
    }

    #endregion

    #region NullToVisibilityConverter Tests

    [TestMethod]
    public void NullToVisibilityConverter_Convert_Null_ReturnsVisible()
    {
        var converter = new NullToVisibilityConverter();
        var result = converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture);
        
        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void NullToVisibilityConverter_Convert_EmptyString_ReturnsVisible()
    {
        var converter = new NullToVisibilityConverter();
        var result = converter.Convert("", typeof(Visibility), null, CultureInfo.InvariantCulture);
        
        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void NullToVisibilityConverter_Convert_NonNull_ReturnsCollapsed()
    {
        var converter = new NullToVisibilityConverter();
        var result = converter.Convert("SomeValue", typeof(Visibility), null, CultureInfo.InvariantCulture);
        
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void NullToVisibilityConverter_Convert_InvertParameter_NonNull_ReturnsVisible()
    {
        var converter = new NullToVisibilityConverter();
        var result = converter.Convert("SomeValue", typeof(Visibility), "invert", CultureInfo.InvariantCulture);
        
        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void NullToVisibilityConverter_ConvertBack_ReturnsDoNothing()
    {
        var converter = new NullToVisibilityConverter();
        var result = converter.ConvertBack(Visibility.Visible, typeof(object), null, CultureInfo.InvariantCulture);
        
        // Should return Binding.DoNothing since Visibility->null conversion is not meaningful
        Assert.AreEqual(Binding.DoNothing, result);
    }

    #endregion

    #region InverseBooleanToVisibilityConverter Tests

    [TestMethod]
    public void InverseBooleanToVisibilityConverter_Convert_True_ReturnsCollapsed()
    {
        var converter = new InverseBooleanToVisibilityConverter();
        var result = converter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture);
        
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void InverseBooleanToVisibilityConverter_Convert_False_ReturnsVisible()
    {
        var converter = new InverseBooleanToVisibilityConverter();
        var result = converter.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture);
        
        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void InverseBooleanToVisibilityConverter_ConvertBack_Collapsed_ReturnsTrue()
    {
        var converter = new InverseBooleanToVisibilityConverter();
        var result = converter.ConvertBack(Visibility.Collapsed, typeof(bool), null, CultureInfo.InvariantCulture);
        
        Assert.IsTrue((bool)result);
    }

    [TestMethod]
    public void InverseBooleanToVisibilityConverter_ConvertBack_Visible_ReturnsFalse()
    {
        var converter = new InverseBooleanToVisibilityConverter();
        var result = converter.ConvertBack(Visibility.Visible, typeof(bool), null, CultureInfo.InvariantCulture);
        
        Assert.IsFalse((bool)result);
    }

    #endregion

    #region SliderValueToWidthConverter Tests

    [TestMethod]
    public void SliderValueToWidthConverter_Convert_Value_ReturnsValue()
    {
        var converter = new SliderValueToWidthConverter();
        var result = converter.Convert(50.0, typeof(double), null, CultureInfo.InvariantCulture);
        
        Assert.AreEqual(50.0, result);
    }

    [TestMethod]
    public void SliderValueToWidthConverter_Convert_Null_ReturnsZero()
    {
        var converter = new SliderValueToWidthConverter();
        var result = converter.Convert(null, typeof(double), null, CultureInfo.InvariantCulture);
        
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void SliderValueToWidthConverter_ConvertBack_Value_ReturnsValue()
    {
        var converter = new SliderValueToWidthConverter();
        var result = converter.ConvertBack(75.0, typeof(double), null, CultureInfo.InvariantCulture);
        
        Assert.AreEqual(75.0, result);
    }

    [TestMethod]
    public void SliderValueToWidthConverter_ConvertBack_Null_ReturnsZero()
    {
        var converter = new SliderValueToWidthConverter();
        var result = converter.ConvertBack(null, typeof(double), null, CultureInfo.InvariantCulture);
        
        Assert.AreEqual(0, result);
    }

    #endregion
}
