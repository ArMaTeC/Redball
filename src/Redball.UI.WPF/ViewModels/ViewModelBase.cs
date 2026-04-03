using CommunityToolkit.Mvvm.ComponentModel;

namespace Redball.UI.ViewModels;

/// <summary>
/// Base ViewModel class implementing INotifyPropertyChanged via CommunityToolkit.Mvvm.
/// Provides SetProperty, OnPropertyChanged, and other helpers out of the box.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Raises the PropertyChanged event for multiple properties
    /// </summary>
    protected void OnPropertiesChanged(params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            OnPropertyChanged(name);
        }
    }
}
