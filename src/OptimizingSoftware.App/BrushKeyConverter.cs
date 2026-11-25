using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OptimizingSoftware.App;

/// <summary>Transforme une clé (« OkBrush ») en la couleur définie dans App.xaml.</summary>
public sealed class BrushKeyConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? Application.Current.TryFindResource(key) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
