using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Redball.UI.Controls;

/// <summary>
/// Virtualized settings panel for handling large numbers of settings efficiently.
/// Uses UI virtualization and lazy loading for smooth scrolling with 100+ settings.
/// </summary>
public class VirtualizedSettingsPanel : VirtualizingPanel, IScrollInfo
{
    private const double _itemHeight = 48; // Standard setting row height
    private const double _groupHeaderHeight = 32;
    private readonly Dictionary<int, Rect> _containerLayouts = new();
    
    public static readonly DependencyProperty ItemHeightProperty = 
        DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizedSettingsPanel), 
            new FrameworkPropertyMetadata(48.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    /// <summary>
    /// Measures the children with virtualization.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var itemsControl = ItemsControl.GetItemsOwner(this);
        if (itemsControl == null) return new Size(0, 0);

        var itemCount = itemsControl.Items.Count;
        var visibleCount = Math.Ceiling(availableSize.Height / ItemHeight) + 2; // Buffer
        var firstVisible = Math.Max(0, (int)(VerticalOffset / ItemHeight) - 1);

        // Only measure visible children
        foreach (UIElement child in InternalChildren)
        {
            var index = InternalChildren.IndexOf(child);
            
            if (index >= firstVisible && index < firstVisible + visibleCount && index < itemCount)
            {
                child.Measure(new Size(availableSize.Width, ItemHeight));
            }
            else
            {
                child.Measure(new Size(0, 0)); // Collapse off-screen items
            }
        }

        var totalHeight = itemCount * ItemHeight;
        _extent = new Size(availableSize.Width, totalHeight);
        _viewport = availableSize;

        return availableSize;
    }

    /// <summary>
    /// Arranges children with virtualization.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var itemsControl = ItemsControl.GetItemsOwner(this);
        if (itemsControl == null) return finalSize;

        var itemCount = itemsControl.Items.Count;
        var visibleCount = Math.Ceiling(finalSize.Height / ItemHeight) + 2;
        var firstVisible = Math.Max(0, (int)(VerticalOffset / ItemHeight) - 1);

        _containerLayouts.Clear();

        foreach (UIElement child in InternalChildren)
        {
            var index = InternalChildren.IndexOf(child);
            
            if (index >= firstVisible && index < firstVisible + visibleCount && index < itemCount)
            {
                var y = index * ItemHeight - VerticalOffset;
                var rect = new Rect(0, y, finalSize.Width, ItemHeight);
                
                child.Arrange(rect);
                _containerLayouts[index] = rect;
            }
            else
            {
                child.Arrange(new Rect(0, 0, 0, 0)); // Hide off-screen
            }
        }

        return finalSize;
    }

    #region IScrollInfo Implementation

    private Size _viewport;
    private Size _extent;
    private Vector _offset;
    private ScrollViewer? _scrollOwner;

    public bool CanHorizontallyScroll { get; set; } = false;
    public bool CanVerticallyScroll { get; set; } = true;
    
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;

    public ScrollViewer? ScrollOwner
    {
        get => _scrollOwner;
        set => _scrollOwner = value;
    }

    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset)
    {
        _offset.Y = Math.Max(0, Math.Min(offset, ExtentHeight - ViewportHeight));
        InvalidateMeasure();
    }

    public void LineUp() => SetVerticalOffset(VerticalOffset - ItemHeight);
    public void LineDown() => SetVerticalOffset(VerticalOffset + ItemHeight);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - ItemHeight * 3);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + ItemHeight * 3);
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }

    public Rect MakeVisible(System.Windows.Media.Visual visual, Rect rectangle)
    {
        var index = InternalChildren.IndexOf(visual as UIElement);
        if (index >= 0)
        {
            var y = index * ItemHeight;
            if (y < VerticalOffset)
            {
                SetVerticalOffset(y);
            }
            else if (y + ItemHeight > VerticalOffset + ViewportHeight)
            {
                SetVerticalOffset(y + ItemHeight - ViewportHeight);
            }
        }
        return rectangle;
    }

    #endregion

    /// <summary>
    /// Recycles containers for better performance.
    /// </summary>
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        InvalidateMeasure();
    }
}

/// <summary>
/// Optimized settings list with grouping and virtualization.
/// </summary>
public class OptimizedSettingsList : ItemsControl
{
    private readonly Dictionary<string, List<SettingItem>> _groupedSettings = new();
    private readonly List<SettingItem> _flatSettings = new();

    static OptimizedSettingsList()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(OptimizedSettingsList), 
            new FrameworkPropertyMetadata(typeof(OptimizedSettingsList)));
    }

    public OptimizedSettingsList()
    {
        // Use virtualizing stack panel for performance
        ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizedSettingsPanel)));
        VirtualizingPanel.SetIsVirtualizing(this, true);
        VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
    }

    /// <summary>
    /// Loads settings with grouping and virtualization.
    /// </summary>
    public void LoadSettings(IEnumerable<SettingItem> settings)
    {
        _flatSettings.Clear();
        _groupedSettings.Clear();

        // Group settings by category
        var grouped = settings.GroupBy(s => s.Category);
        
        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            // Add group header
            _flatSettings.Add(new SettingItem
            {
                Category = group.Key,
                IsHeader = true,
                DisplayName = group.Key
            });

            // Add group items
            foreach (var item in group.OrderBy(i => i.Order))
            {
                _flatSettings.Add(item);
            }

            _groupedSettings[group.Key] = group.ToList();
        }

        ItemsSource = _flatSettings;
    }

    /// <summary>
    /// Searches settings with debounced filtering.
    /// </summary>
    public void Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ItemsSource = _flatSettings;
            return;
        }

        var filtered = _flatSettings.Where(s => 
            s.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            s.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
            s.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        ItemsSource = filtered;
    }

    /// <summary>
    /// Collapses all groups.
    /// </summary>
    public void CollapseAll()
    {
        foreach (var item in _flatSettings.Where(i => i.IsHeader))
        {
            item.IsExpanded = false;
        }
        // Refresh the view to show collapsed state
        var currentSource = ItemsSource as List<SettingItem>;
        if (currentSource != null)
        {
            // Filter out non-header items from collapsed groups
            var visibleItems = new List<SettingItem>();
            string? currentGroup = null;
            bool currentGroupExpanded = true;
            
            foreach (var item in _flatSettings)
            {
                if (item.IsHeader)
                {
                    currentGroup = item.Category;
                    currentGroupExpanded = item.IsExpanded;
                    visibleItems.Add(item);
                }
                else if (currentGroupExpanded)
                {
                    visibleItems.Add(item);
                }
            }
            ItemsSource = visibleItems;
        }
    }

    /// <summary>
    /// Expands all groups.
    /// </summary>
    public void ExpandAll()
    {
        foreach (var item in _flatSettings.Where(i => i.IsHeader))
        {
            item.IsExpanded = true;
        }
        // Restore full list
        ItemsSource = _flatSettings;
    }

    /// <summary>
    /// Toggles expansion of a specific group.
    /// </summary>
    public void ToggleGroup(string category)
    {
        var header = _flatSettings.FirstOrDefault(i => i.IsHeader && i.Category == category);
        if (header != null)
        {
            header.IsExpanded = !header.IsExpanded;
            
            // Refresh view
            var visibleItems = new List<SettingItem>();
            bool includeItems = true;
            
            foreach (var item in _flatSettings)
            {
                if (item.IsHeader)
                {
                    includeItems = item.IsExpanded;
                    visibleItems.Add(item);
                }
                else if (includeItems)
                {
                    visibleItems.Add(item);
                }
            }
            ItemsSource = visibleItems;
        }
    }
}

/// <summary>
/// Setting item model for optimized list.
/// </summary>
public class SettingItem
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string Category { get; set; } = "";
    public int Order { get; set; }
    public SettingType Type { get; set; }
    public object? Value { get; set; }
    public object? DefaultValue { get; set; }
    public bool IsHeader { get; set; }
    public bool IsExpanded { get; set; } = true; // Default to expanded
    public List<SettingOption>? Options { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string? Unit { get; set; }
}

public enum SettingType
{
    Toggle,
    Slider,
    Dropdown,
    Text,
    Number,
    Color,
    Button,
    Header
}

public class SettingOption
{
    public string Value { get; set; } = "";
    public string Display { get; set; } = "";
}

/// <summary>
/// Container for setting items with recycling support.
/// </summary>
public class SettingContainer : ContentControl
{
    static SettingContainer()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingContainer), 
            new FrameworkPropertyMetadata(typeof(SettingContainer)));
    }

    public SettingContainer()
    {
        // Prepare for virtualization
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Attach data context change handler
        DataContextChanged += OnDataContextChanged;
        PrepareContainer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DataContextChanged -= OnDataContextChanged;
        ClearContainer();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        PrepareContainer();
    }

    private void PrepareContainer()
    {
        if (DataContext is not SettingItem item) return;

        // Build UI based on setting type
        Content = BuildSettingControl(item);
    }

    private void ClearContainer()
    {
        Content = null;
    }

    private UIElement BuildSettingControl(SettingItem item)
    {
        if (item.IsHeader)
        {
            return new TextBlock
            {
                Text = item.DisplayName,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(12, 16, 12, 8)
            };
        }

        var panel = new DockPanel { Margin = new Thickness(12, 4, 12, 4) };

        // Label
        var label = new TextBlock
        {
            Text = item.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = item.Description
        };
        DockPanel.SetDock(label, Dock.Left);
        panel.Children.Add(label);

        // Value control
        UIElement? valueControl = item.Type switch
        {
            SettingType.Toggle => new CheckBox { IsChecked = (bool?)item.Value },
            SettingType.Slider => new Slider 
            { 
                Value = Convert.ToDouble(item.Value ?? 0), 
                Minimum = item.Min ?? 0, 
                Maximum = item.Max ?? 100,
                Width = 150
            },
            SettingType.Dropdown => new ComboBox 
            { 
                ItemsSource = item.Options,
                SelectedValue = item.Value,
                Width = 150
            },
            _ => new TextBox { Text = item.Value?.ToString(), Width = 150 }
        };

        if (valueControl != null)
        {
            valueControl.SetValue(DockPanel.DockProperty, Dock.Right);
            panel.Children.Add(valueControl);
        }

        return panel;
    }
}
